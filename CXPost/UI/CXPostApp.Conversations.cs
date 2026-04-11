using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI;

public partial class CXPostApp
{
    private bool _isPopulatingThreadedList;

    /// <summary>
    /// Populates the message table with threaded view (collapsed thread rows).
    /// </summary>
    private void PopulateThreadedMessageList(List<MailMessage> messages)
    {
        if (_messageTable == null || _isPopulatingThreadedList) return;
        _isPopulatingThreadedList = true;
        try
        {
            // Capture current selection so we can restore it after rebuild
            string? selectedThreadId = null;
            uint? selectedChildUid = null;
            var selIdx = _messageTable.SelectedRowIndex;
            if (selIdx >= 0 && selIdx < _messageTable.RowCount)
            {
                var selRow = _messageTable.GetRow(selIdx);
                if (selRow.Tag is ThreadSummary ts) selectedThreadId = ts.ThreadId;
                else if (selRow.Tag is MailMessage m)
                {
                    selectedChildUid = m.Uid;
                    selectedThreadId = m.ThreadId;
                }
            }

            _threadSummaries = ConversationGrouper.Group(messages);
            _messageTable.ClearRows();

            // Prune expanded IDs that no longer exist (filtered out, deleted, etc.)
            var currentThreadIds = new HashSet<string>(_threadSummaries.Select(t => t.ThreadId));
            _expandedThreadIds.IntersectWith(currentThreadIds);

            foreach (var thread in _threadSummaries)
            {
                var headerRow = BuildThreadHeaderRow(thread);
                _messageTable.AddRow(headerRow);

                if (_expandedThreadIds.Contains(thread.ThreadId) && thread.IsThread)
                {
                    foreach (var msg in thread.Messages)
                    {
                        var childRow = BuildThreadChildRow(msg);
                        _messageTable.AddRow(childRow);
                    }
                }
            }

            // Restore selection by matching thread ID / child UID
            if (selectedThreadId != null)
            {
                for (int i = 0; i < _messageTable.RowCount; i++)
                {
                    var row = _messageTable.GetRow(i);
                    if (selectedChildUid.HasValue && row.Tag is MailMessage m && m.Uid == selectedChildUid.Value)
                    {
                        _messageTable.SelectedRowIndex = i;
                        break;
                    }
                    if (selectedChildUid == null && row.Tag is ThreadSummary ts && ts.ThreadId == selectedThreadId)
                    {
                        _messageTable.SelectedRowIndex = i;
                        break;
                    }
                }
            }
        }
        finally { _isPopulatingThreadedList = false; }
    }

    /// <summary>
    /// Rebuilds the threaded table from cached _threadSummaries without reloading from DB.
    /// Used for expand/collapse which only changes display, not data.
    /// </summary>
    private void RebuildThreadedTable()
    {
        if (_messageTable == null || _threadSummaries == null || _isPopulatingThreadedList) return;
        _isPopulatingThreadedList = true;
        try
        {
            // Capture current selection so we can restore it after rebuild
            string? selectedThreadId = null;
            uint? selectedChildUid = null;
            var selIdx = _messageTable.SelectedRowIndex;
            if (selIdx >= 0 && selIdx < _messageTable.RowCount)
            {
                var selRow = _messageTable.GetRow(selIdx);
                if (selRow.Tag is ThreadSummary ts) selectedThreadId = ts.ThreadId;
                else if (selRow.Tag is MailMessage m)
                {
                    selectedChildUid = m.Uid;
                    selectedThreadId = m.ThreadId;
                }
            }

            _messageTable.ClearRows();
            foreach (var thread in _threadSummaries)
            {
                _messageTable.AddRow(BuildThreadHeaderRow(thread));
                if (_expandedThreadIds.Contains(thread.ThreadId) && thread.IsThread)
                {
                    foreach (var msg in thread.Messages)
                        _messageTable.AddRow(BuildThreadChildRow(msg));
                }
            }

            // Restore selection
            if (selectedThreadId != null)
            {
                for (int i = 0; i < _messageTable.RowCount; i++)
                {
                    var row = _messageTable.GetRow(i);
                    if (selectedChildUid.HasValue && row.Tag is MailMessage m && m.Uid == selectedChildUid.Value)
                    {
                        _messageTable.SelectedRowIndex = i;
                        break;
                    }
                    if (selectedChildUid == null && row.Tag is ThreadSummary ts && ts.ThreadId == selectedThreadId)
                    {
                        _messageTable.SelectedRowIndex = i;
                        break;
                    }
                }
            }
        }
        finally { _isPopulatingThreadedList = false; }
    }

    private TableRow BuildThreadHeaderRow(ThreadSummary thread)
    {
        var newest = thread.NewestMessage;
        var isUnread = thread.HasUnread;
        var isExpanded = _expandedThreadIds.Contains(thread.ThreadId);

        var star = thread.HasFlagged ? "[yellow]\u2605[/]" : "[grey35]\u2606[/]";
        var clip = thread.HasAttachments ? "[grey70]\U0001f4ce[/]" : "";

        var senderText = newest.FromName ?? newest.FromAddress ?? "Unknown";
        var threadPrefix = "";
        if (thread.IsThread)
        {
            var arrow = isExpanded ? "\u25be" : "\u25b8";
            threadPrefix = $"[{ColorScheme.PrimaryMarkup}]{arrow}{thread.Count}[/] ";
        }
        var from = isUnread
            ? $"{threadPrefix}[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(senderText)}[/]"
            : $"{threadPrefix}[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(senderText)}[/]";

        var subjectText = thread.BaseSubject;
        var subject = isUnread
            ? $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(subjectText)}[/]"
            : $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(subjectText)}[/]";

        var date = FormatDate(newest.Date);
        var row = new TableRow(new List<string> { star, clip, from, subject, date })
        {
            Tag = thread
        };
        return row;
    }

    private TableRow BuildThreadChildRow(MailMessage msg)
    {
        var star = msg.IsFlagged ? "[yellow]\u2605[/]" : "[grey35]\u2606[/]";
        var clip = msg.HasAttachments ? "[grey70]\U0001f4ce[/]" : "";
        var from = msg.IsRead
            ? $"[{ColorScheme.ReadMarkup}]    {MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]"
            : $"[{ColorScheme.UnreadMarkup}]    {MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]";
        var subject = msg.IsRead
            ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]"
            : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]";
        var date = FormatDate(msg.Date);
        var row = new TableRow(new List<string> { star, clip, from, subject, date })
        {
            Tag = msg
        };
        return row;
    }

    /// <summary>
    /// Shows a full conversation in the reading pane.
    /// All messages in the thread are displayed chronologically.
    /// The highlighted message gets a visual indicator.
    /// </summary>
    private bool _isShowingConversation;

    private void ShowConversationPreview(ThreadSummary thread, MailMessage? highlightedMessage = null)
    {
        if (_readingPane == null || _isShowingConversation) return;
        _isShowingConversation = true;
        try
        {
        _readingPane.ClearContents();
        _readingPane.ScrollToTop();

        highlightedMessage ??= thread.NewestMessage;

        // Thread header
        var participants = string.Join(", ", thread.Messages
            .Select(m => m.FromName ?? m.FromAddress ?? "Unknown")
            .Distinct());
        var headerLines = new List<string>
        {
            "",
            $"  [{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(thread.BaseSubject)}[/]",
            $"  [{ColorScheme.MutedMarkup}]{thread.Count} message{(thread.Count != 1 ? "s" : "")} \u00b7 {MarkupParser.Escape(participants)}[/]",
            ""
        };
        var headerControl = Controls.Markup().Build();
        headerControl.HorizontalAlignment = HorizontalAlignment.Stretch;
        headerControl.BackgroundColor = ColorScheme.HeaderSectionBg;
        headerControl.SetContent(headerLines);
        _readingPane.AddControl(headerControl);

        // Consolidated attachments from all messages in thread
        var msgsWithAttachments = thread.Messages
            .Where(m => m.Attachments != null && m.Attachments.Count > 0)
            .ToList();
        if (msgsWithAttachments.Count > 0)
        {
            foreach (var attMsg in msgsWithAttachments)
                AddAttachmentControls(attMsg);
        }

        // Empty line after header/attachments
        _readingPane.AddControl(Controls.Markup("").Build());

        // Each message in chronological order (Messages list is already sorted oldest-first by ConversationGrouper)
        IWindowControl? highlightedControl = null;
        foreach (var msg in thread.Messages)
        {
            var isHighlighted = msg.Uid == highlightedMessage.Uid
                && msg.FolderId == highlightedMessage.FolderId;

            // Message separator with sender and date
            var senderName = msg.FromName ?? msg.FromAddress ?? "Unknown";
            var localDate = msg.Date.Kind == DateTimeKind.Utc ? msg.Date.ToLocalTime() : msg.Date;
            var dateStr = localDate.ToString("MMM d, yyyy h:mm tt");
            var indicator = isHighlighted ? " \u25c0" : "";
            var sepColor = isHighlighted ? ColorScheme.PrimaryMarkup : "grey50";
            var separator = Controls.Markup(
                $"[{sepColor}]\u2500\u2500\u2500 {MarkupParser.Escape(senderName)} \u00b7 {dateStr}{indicator} \u2500\u2500\u2500[/]")
                .Build();
            separator.HorizontalAlignment = HorizontalAlignment.Stretch;
            separator.Margin = new Margin(2, 0, 2, 0);
            if (isHighlighted) highlightedControl = separator;
            _readingPane.AddControl(separator);

            // Message body
            if (msg.BodyFetched && msg.BodyPlain != null)
            {
                var body = msg.BodyPlain;
                List<string> bodyLines;

                if (MessageFormatter.IsHtml(body))
                {
                    var markup = Components.HtmlConverter.ToMarkup(body);
                    bodyLines = markup.Split('\n').ToList();
                }
                else
                {
                    bodyLines = body.Split('\n').Select(l => MarkupParser.Escape(l)).ToList();
                }

                var bodyControl = Controls.Markup().Build();
                bodyControl.HorizontalAlignment = HorizontalAlignment.Stretch;
                bodyControl.Margin = new Margin(2, 0, 2, 0);
                if (isHighlighted)
                    bodyControl.BackgroundColor = ColorScheme.HeaderSectionBg;
                bodyControl.SetContent(bodyLines);
                _readingPane.AddControl(bodyControl);
            }
            else
            {
                var loading = Controls.Markup($"[{ColorScheme.MutedMarkup}]Loading...[/]").Build();
                loading.Margin = new Margin(2, 0, 2, 0);
                _readingPane.AddControl(loading);
            }

            // Empty line between messages
            _readingPane.AddControl(Controls.Markup("").Build());
        }

        // Scroll to the highlighted message
        if (highlightedControl != null)
            _readingPane.ScrollChildIntoView(highlightedControl);
        }
        finally { _isShowingConversation = false; }
    }
}
