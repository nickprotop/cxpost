using System.Collections.Concurrent;
using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Rendering;
using CXPost.Coordinators;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;
using CXPost.UI.Dialogs;

namespace CXPost.UI;

public partial class CXPostApp
{
    private async Task SendWithProgressAsync(ComposeResult result)
    {
        var account = _config.Accounts.FirstOrDefault(a => a.Id == result.AccountId);
        if (account == null)
        {
            EnqueueUiAction(() => ShowError("Account not found"));
            return;
        }

        var sendMsgId = "send-progress";
        try
        {
            EnqueueUiAction(() =>
                ReplaceMessage(sendMsgId, $"Connecting to {account.SmtpHost}..."));

            EnqueueUiAction(() =>
                ReplaceMessage(sendMsgId, $"Sending to {result.To}..."));

            await _composeCoordinator.SendAsync(
                account, result.FromName, result.To, result.Cc, result.Subject,
                result.Body, result.AttachmentPaths, _cts.Token);

            EnqueueUiAction(() =>
            {
                DismissMessage(sendMsgId);
                ShowSuccess($"Message sent to {result.To}");
            });
        }
        catch (Exception ex)
        {
            EnqueueUiAction(() =>
            {
                DismissMessage(sendMsgId);
                ShowError($"Send failed: {ex.Message}");
            });
        }
    }

    private async Task SendReplyWithAttachmentsAsync(ComposeResult result, Account account, MailMessage msg)
    {
        if (result.IncludeOriginalAttachments && msg.HasAttachments
            && msg.Attachments != null && msg.Attachments.Count > 0)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"cxpost-reply-{Guid.NewGuid():N}");
            var progressId = "reply-progress";
            try
            {
                EnqueueUiAction(() => ReplaceMessage(progressId, "Fetching attachments..."));
                var paths = await _composeCoordinator.FetchMessageAttachmentsAsync(
                    account, msg, tempDir, _cts.Token);

                var totalSize = paths.Sum(p => new FileInfo(p).Length);
                if (totalSize > 20 * 1024 * 1024)
                {
                    var sizeMb = totalSize / (1024.0 * 1024.0);
                    var confirm = await new ConfirmDialog(
                        "Large Attachments",
                        $"Total attachment size is {sizeMb:F1} MB. Continue sending?")
                        .ShowAsync(_ws);
                    if (!confirm)
                    {
                        EnqueueUiAction(() =>
                        {
                            DismissMessage(progressId);
                            ShowInfo("Reply cancelled.");
                        });
                        return;
                    }
                }

                var allPaths = new List<string>(result.AttachmentPaths);
                allPaths.AddRange(paths);

                EnqueueUiAction(() => ReplaceMessage(progressId, "Sending reply..."));
                await _composeCoordinator.SendAsync(
                    account, result.FromName, result.To, result.Cc,
                    result.Subject, result.Body, allPaths, _cts.Token);

                EnqueueUiAction(() =>
                {
                    DismissMessage(progressId);
                    ShowSuccess($"Reply sent to {result.To}");
                });
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() =>
                {
                    DismissMessage(progressId);
                    ShowError($"Reply failed: {ex.Message}");
                });
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { }
            }
        }
        else
        {
            await SendWithProgressAsync(result);
        }
    }
}
