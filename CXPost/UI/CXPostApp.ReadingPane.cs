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
using SharpConsoleUI.Windows;
using CXPost.Coordinators;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;
using CXPost.UI.Dialogs;

namespace CXPost.UI;

public partial class CXPostApp
{
    public void ShowMessagePreview(MailMessage msg)
    {
        if (_readingPane == null) return;

        _readingPane.ClearContents();

        // Header
        var headerLines = new List<string>
        {
            "",
            $"  [{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]",
            "",
            $"  [{ColorScheme.MutedMarkup}]From:[/]  {MarkupParser.Escape(msg.FromName ?? "")} <{MarkupParser.Escape(msg.FromAddress ?? "")}>",
            $"  [{ColorScheme.MutedMarkup}]Date:[/]  {msg.Date:MMMM d, yyyy h:mm tt}",
            $"  [{ColorScheme.MutedMarkup}]To:[/]    {MarkupParser.Escape(MessageFormatter.FormatAddresses(msg.ToAddresses))}",
        };
        if (!string.IsNullOrEmpty(msg.CcAddresses))
        {
            headerLines.Add($"  [{ColorScheme.MutedMarkup}]Cc:[/]    {MarkupParser.Escape(MessageFormatter.FormatAddresses(msg.CcAddresses))}");
        }
        headerLines.Add("");
        var headerControl = Controls.Markup().Build();
        headerControl.HorizontalAlignment = HorizontalAlignment.Stretch;
        headerControl.SetContent(headerLines);
        _readingPane.AddControl(headerControl);

        // Attachment section
        if (msg.Attachments != null && msg.Attachments.Count > 0)
            AddAttachmentControls(msg);
        else if (msg.HasAttachments && msg.Attachments == null)
        {
            var loadingAtt = Controls.Markup(
                $"  [{ColorScheme.MutedMarkup}]\U0001f4ce Loading attachments...[/]").Build();
            _readingPane.AddControl(loadingAtt);
        }

        // Body
        if (msg.BodyFetched && msg.BodyPlain != null)
        {
            var bodyLines = new List<string>
            {
                $"  [grey23]{"".PadRight(60, '\u2500')}[/]",
                ""
            };
            var body = msg.BodyPlain;
            if (MessageFormatter.IsHtml(body))
            {
                var markup = Components.HtmlConverter.ToMarkup(body);
                bodyLines.AddRange(markup.Split('\n').Select(l => $"  {l}"));
            }
            else
            {
                bodyLines.AddRange(body.Split('\n').Select(l => $"  {MarkupParser.Escape(l)}"));
            }
            var bodyControl = Controls.Markup().Build();
            bodyControl.HorizontalAlignment = HorizontalAlignment.Stretch;
            bodyControl.SetContent(bodyLines);
            _readingPane.AddControl(bodyControl);
        }
        else
        {
            var loading = Controls.Markup($"  [{ColorScheme.MutedMarkup}]Loading message body...[/]").Build();
            _readingPane.AddControl(loading);
        }

        if (!_isSearchActive && GetCheckedCount() == 0 && (_readingPane.CanScrollDown || _readingPane.CanScrollUp))
            SetRightPanelHeader("[grey70]Messages[/] [grey50](\u2191\u2193 to scroll)[/]", showSyncAction: true, showFlaggedFilter: true);
    }

    public void ClearReadingPane()
    {
        if (_readingPane == null) return;
        _readingPane.ClearContents();
        var placeholder = Controls.Markup($"  [{ColorScheme.MutedMarkup}]Select a message to read[/]").Build();
        placeholder.HorizontalAlignment = HorizontalAlignment.Stretch;
        _readingPane.AddControl(placeholder);
    }

    private void TriggerReadingPaneFadeIn()
    {
        if (_readingPane == null || _mainWindow == null) return;

        float fadeIntensity = 0.35f;
        WindowRenderer.BufferPaintDelegate? handler = null;
        handler = (buffer, dirtyRegion, clipRect) =>
        {
            if (fadeIntensity <= 0.01f) return;
            var paneRect = new LayoutRect(
                _readingPane.ActualX, _readingPane.ActualY,
                _readingPane.ActualWidth, _readingPane.ActualHeight);
            ColorBlendHelper.ApplyColorOverlay(buffer, Color.Black, fadeIntensity, 0.5f, paneRect);
        };

        _mainWindow.PostBufferPaint += handler;
        _ws.Animations.Animate(
            from: 0.35f, to: 0.0f,
            duration: TimeSpan.FromMilliseconds(250),
            easing: EasingFunctions.EaseOut,
            onUpdate: t =>
            {
                fadeIntensity = t;
                _mainWindow?.Invalidate(redrawAll: true);
            },
            onComplete: () =>
            {
                fadeIntensity = 0f;
                _mainWindow!.PostBufferPaint -= handler;
            });
    }

    private void AddAttachmentControls(MailMessage msg)
    {
        if (_readingPane == null || msg.Attachments == null) return;

        _readingPane.AddControl(Controls.Markup(
            $"  [{ColorScheme.PrimaryMarkup}]\U0001f4ce Attachments ({msg.Attachments.Count})[/]")
            .Build());

        var rule = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
        _readingPane.AddControl(rule);

        foreach (var att in msg.Attachments)
        {
            var sizeStr = FormatFileSize(att.Size);
            var idx = att.Index;
            var fileName = att.FileName;

            var attLabel = Controls.Markup(
                $"  [{ColorScheme.PrimaryMarkup}][[{idx + 1}]][/] {MarkupParser.Escape(fileName)}  [grey50]{sizeStr}[/]")
                .WithMargin(2, 0, 2, 0)
                .Build();
            _readingPane.AddControl(attLabel);

            var attActions = Controls.StatusBar()
                .AddLeft($"{idx + 1}", "Save", () => SaveAttachmentQuick(msg, idx, fileName))
                .AddLeft($"Ctrl+{idx + 1}", "Save As", () => SaveAttachmentAs(msg, idx))
                .WithMargin(4, 0, 2, 0)
                .Build();
            attActions.BackgroundColor = Color.Transparent;
            _readingPane.AddControl(attActions);
        }

        var rule2 = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
        _readingPane.AddControl(rule2);

        if (msg.Attachments.Count > 1)
        {
            var actionBar = Controls.StatusBar()
                .AddLeft("A", "Save All", () => SaveAllAttachments(msg))
                .AddLeft("Ctrl+A", "Save All to...", () => SaveAllAttachmentsAs(msg))
                .WithMargin(2, 0, 2, 0)
                .Build();
            actionBar.BackgroundColor = Color.Transparent;
            _readingPane.AddControl(actionBar);
        }
    }

    private void SaveAttachmentQuick(MailMessage msg, int index, string fileName)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null) return;

        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var targetPath = Path.Combine(downloadsDir, fileName);
        var msgId = $"save-{msg.Uid}-{index}";

        ReplaceMessage(msgId, $"Saving {fileName}...");

        _ = Task.Run(async () =>
        {
            try
            {
                using var imap = await _imapFactory.CreateConnectionAsync(account, _cts.Token);
                await imap.SaveAttachmentAsync(folder.Path, msg.Uid, index, targetPath, _cts.Token);
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {fileName} to ~/Downloads/",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private void SaveAttachmentAs(MailMessage msg, int index)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null || msg.Attachments == null) return;

        var fileName = msg.Attachments[index].FileName;

        _ = Task.Run(async () =>
        {
            var dir = await SharpConsoleUI.Dialogs.FileDialogs.ShowFolderPickerAsync(_ws);
            if (dir == null) return;

            var msgId = $"saveas-{msg.Uid}-{index}";
            EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving {fileName}..."));

            var targetPath = Path.Combine(dir, fileName);
            try
            {
                using var imap = await _imapFactory.CreateConnectionAsync(account, _cts.Token);
                await imap.SaveAttachmentAsync(folder.Path, msg.Uid, index, targetPath, _cts.Token);
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {fileName} to {dir}",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private void SaveAllAttachments(MailMessage msg)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null || msg.Attachments == null) return;

        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var msgId = $"saveall-{msg.Uid}";
        var total = msg.Attachments.Count;

        ReplaceMessage(msgId, $"Saving 1/{total} attachments...");

        _ = Task.Run(async () =>
        {
            try
            {
                using var imap = await _imapFactory.CreateConnectionAsync(account, _cts.Token);
                for (var i = 0; i < msg.Attachments.Count; i++)
                {
                    var att = msg.Attachments[i];
                    var progress = i + 1;
                    EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving {progress}/{total}: {att.FileName}..."));
                    var targetPath = Path.Combine(downloadsDir, att.FileName);
                    await imap.SaveAttachmentAsync(folder.Path, msg.Uid, att.Index, targetPath, _cts.Token);
                }
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {total} attachments to ~/Downloads/",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private void SaveAllAttachmentsAs(MailMessage msg)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null || msg.Attachments == null) return;

        var total = msg.Attachments.Count;

        _ = Task.Run(async () =>
        {
            var dir = await SharpConsoleUI.Dialogs.FileDialogs.ShowFolderPickerAsync(_ws);
            if (dir == null) return;

            var msgId = $"saveallas-{msg.Uid}";
            EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving 1/{total} attachments..."));

            try
            {
                using var imap = await _imapFactory.CreateConnectionAsync(account, _cts.Token);
                for (var i = 0; i < msg.Attachments.Count; i++)
                {
                    var att = msg.Attachments[i];
                    var progress = i + 1;
                    EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving {progress}/{total}: {att.FileName}..."));
                    var targetPath = Path.Combine(dir, att.FileName);
                    await imap.SaveAttachmentAsync(folder.Path, msg.Uid, att.Index, targetPath, _cts.Token);
                }
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {total} attachments to {dir}",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void UpdatePreviewHeader(MailMessage? msg = null)
    {
        if (_previewPanelHeader == null) return;

        if (msg != null && _messageTable != null)
        {
            var selectedIdx = _messageTable.SelectedRowIndex + 1;
            var total = _messageTable.RowCount;
            var status = msg.IsRead ? "[grey50]Read[/]" : "[yellow]Unread[/]";
            var date = msg.Date.ToString("MMM d, yyyy 'at' h:mm tt");
            _previewPanelHeader.SetContent(
                [$"[grey70]{selectedIdx} of {total}[/]  {status}  [grey50]{date}[/]"]);
        }
        else
        {
            _previewPanelHeader.SetContent(["[grey70]Preview[/]"]);
        }
    }
}
