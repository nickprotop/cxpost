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
        headerControl.Tag = "header";
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
            // Separator between header/attachments and body
            var sepRule = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
            _readingPane.AddControl(sepRule);

            var body = msg.BodyPlain;

            if (MessageFormatter.IsHtml(body))
            {
                var markup = Components.HtmlConverter.ToMarkup(body);
                var bodyLines = markup.Split('\n').Select(l => $"  {l}").ToList();
                AddHtmlSegmentedControls(bodyLines);
            }
            else
            {
                var bodyLines = body.Split('\n').Select(l => $"  {MarkupParser.Escape(l)}").ToList();
                AddPlainTextSegmentedControls(bodyLines);
            }
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

    private void AddPlainTextSegmentedControls(List<string> lines)
    {
        if (_readingPane == null) return;

        var segments = Components.EmailBodyParser.Parse(lines);
        foreach (var segment in segments)
        {
            var control = Controls.Markup().Build();
            control.HorizontalAlignment = HorizontalAlignment.Stretch;

            switch (segment.Type)
            {
                case Components.EmailSegmentType.Quote:
                    control.Tag = "quote";
                    var quoteLines = segment.Lines.Select(l =>
                    {
                        var trimmed = l.TrimStart();
                        // Strip "> " prefix if present, then re-format
                        var content = trimmed.StartsWith("> ") ? trimmed[2..] : trimmed;
                        return $"  [{ColorScheme.QuoteBorderMarkup}]\u258e[/] [{ColorScheme.QuoteTextMarkup}]{content}[/]";
                    }).ToList();
                    control.SetContent(quoteLines);
                    break;

                case Components.EmailSegmentType.Signature:
                    control.Tag = "signature";
                    var sigLines = segment.Lines.Select(l =>
                        $"  [{ColorScheme.SignatureMarkup}]{l.TrimStart()}[/]").ToList();
                    control.SetContent(sigLines);
                    break;

                default:
                    control.SetContent(segment.Lines);
                    break;
            }

            _readingPane.AddControl(control);
        }
    }

    private void AddHtmlSegmentedControls(List<string> lines)
    {
        if (_readingPane == null) return;

        // Split on blockquote markers, then parse remaining for signatures
        var segments = new List<Components.EmailSegment>();
        var currentLines = new List<string>();
        bool inQuote = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == Components.HtmlConverter.BlockquoteStartMarker)
            {
                if (currentLines.Count > 0)
                {
                    segments.AddRange(Components.EmailBodyParser.Parse(currentLines));
                    currentLines.Clear();
                }
                inQuote = true;
                continue;
            }
            if (trimmed == Components.HtmlConverter.BlockquoteEndMarker)
            {
                if (currentLines.Count > 0)
                    segments.Add(new Components.EmailSegment(Components.EmailSegmentType.Quote, new List<string>(currentLines)));
                currentLines.Clear();
                inQuote = false;
                continue;
            }

            currentLines.Add(line);
        }

        if (currentLines.Count > 0)
        {
            if (inQuote)
                segments.Add(new Components.EmailSegment(Components.EmailSegmentType.Quote, currentLines));
            else
                segments.AddRange(Components.EmailBodyParser.Parse(currentLines));
        }

        foreach (var segment in segments)
        {
            var control = Controls.Markup().Build();
            control.HorizontalAlignment = HorizontalAlignment.Stretch;

            switch (segment.Type)
            {
                case Components.EmailSegmentType.Quote:
                    control.Tag = "quote";
                    var quoteLines = segment.Lines.Select(l =>
                        $"  [{ColorScheme.QuoteBorderMarkup}]\u258e[/] [{ColorScheme.QuoteTextMarkup}]{l.TrimStart()}[/]").ToList();
                    control.SetContent(quoteLines);
                    break;

                case Components.EmailSegmentType.Signature:
                    control.Tag = "signature";
                    var sigLines = segment.Lines.Select(l =>
                        $"  [{ColorScheme.SignatureMarkup}]{l.TrimStart()}[/]").ToList();
                    control.SetContent(sigLines);
                    break;

                default:
                    control.SetContent(segment.Lines);
                    break;
            }

            _readingPane.AddControl(control);
        }
    }

    private static string GetFileTypeIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "\U0001f4c4",
            ".doc" or ".docx" => "\U0001f4dd",
            ".xls" or ".xlsx" => "\U0001f4ca",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".bmp" or ".webp" => "\U0001f5bc",
            ".zip" or ".tar" or ".gz" or ".rar" or ".7z" => "\U0001f4e6",
            ".mp3" or ".wav" or ".ogg" or ".flac" => "\U0001f3b5",
            ".mp4" or ".mov" or ".avi" or ".mkv" => "\U0001f3ac",
            _ => "\U0001f4ce"
        };
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
            var icon = GetFileTypeIcon(fileName);

            var attBar = Controls.StatusBar()
                .AddLeftText($" {icon} {MarkupParser.Escape(fileName)}  [grey50]{sizeStr}[/]")
                .AddLeft($"{idx + 1}", "Save", () => SaveAttachmentQuick(msg, idx, fileName))
                .AddLeft($"Ctrl+{idx + 1}", "Save As", () => SaveAttachmentAs(msg, idx))
                .WithMargin(2, 0, 2, 0)
                .Build();
            attBar.BackgroundColor = Color.Transparent;
            _readingPane.AddControl(attBar);
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

        _previewPanelHeader.ClearAll();

        if (msg != null && _messageTable != null)
        {
            var selectedIdx = _messageTable.SelectedRowIndex + 1;
            var total = _messageTable.RowCount;
            var status = msg.IsRead ? "[grey50]Read[/]" : "[yellow]Unread[/]";
            var date = msg.Date.ToString("MMM d, yyyy 'at' h:mm tt");
            _previewPanelHeader.AddLeftText(
                $"[grey70]{selectedIdx} of {total}[/]  {status}  [grey50]{date}[/]");
        }
        else
        {
            _previewPanelHeader.AddLeftText("[grey70]Preview[/]");
        }

        // View controls are in the bottom bar — preview header stays clean
    }
}
