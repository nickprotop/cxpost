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
    private int _populatedFolderId;

    public void PopulateMessageList(List<MailMessage> messages)
    {
        if (_messageTable == null) return;

        if (_isFlaggedFilterActive)
            messages = messages.Where(m => m.IsFlagged).ToList();

        // During search: results span multiple folders — use FolderId+Uid as composite key
        // for in-place updates instead of clearing and rebuilding
        if (_isSearchActive)
        {
            ImapLogger.Debug($"PopulateMessageList (search): {messages.Count} messages");

            var incomingKeys = new HashSet<(int FolderId, uint Uid)>(
                messages.Select(m => (m.FolderId, m.Uid)));

            // Remove rows no longer in the result set (reverse to keep indices stable)
            for (var i = _messageTable.RowCount - 1; i >= 0; i--)
            {
                var row = _messageTable.GetRow(i);
                if (row.Tag is MailMessage m && !incomingKeys.Contains((m.FolderId, m.Uid)))
                    _messageTable.RemoveRow(i);
            }

            // Build lookup of existing rows by composite key
            var searchExisting = new Dictionary<(int, uint), int>();
            for (var i = 0; i < _messageTable.RowCount; i++)
            {
                var row = _messageTable.GetRow(i);
                if (row.Tag is MailMessage m)
                    searchExisting[(m.FolderId, m.Uid)] = i;
            }

            // Update existing rows in-place, insert new ones
            for (var i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                var (star, clip, from, subject, date) = FormatMessageRow(msg);
                var key = (msg.FolderId, msg.Uid);

                if (searchExisting.TryGetValue(key, out var rowIdx))
                {
                    _messageTable.UpdateCell(rowIdx, 0, star);
                    _messageTable.UpdateCell(rowIdx, 1, clip);
                    _messageTable.UpdateCell(rowIdx, 2, from);
                    _messageTable.UpdateCell(rowIdx, 3, subject);
                    _messageTable.UpdateCell(rowIdx, 4, date);
                    _messageTable.GetRow(rowIdx).Tag = msg;
                }
                else
                {
                    var row = new TableRow(star, clip, from, subject, date) { Tag = msg };
                    _messageTable.InsertRow(i, row);

                    searchExisting.Clear();
                    for (var j = 0; j < _messageTable.RowCount; j++)
                    {
                        var r = _messageTable.GetRow(j);
                        if (r.Tag is MailMessage rm)
                            searchExisting[(rm.FolderId, rm.Uid)] = j;
                    }
                }
            }
            return;
        }

        // Determine folder ID from messages
        var folderId = messages.Count > 0 ? messages[0].FolderId : _populatedFolderId;
        var folderChanged = folderId != _populatedFolderId;

        ImapLogger.Debug($"PopulateMessageList: {messages.Count} messages, current rows={_messageTable.RowCount}, folder={folderId}, changed={folderChanged}");

        // On folder change, clear and rebuild (UIDs are per-folder, diff would be wrong)
        if (folderChanged)
        {
            _populatedFolderId = folderId;
            _messageTable.ClearRows();
            foreach (var msg in messages)
            {
                var (star, clip, from, subject, date) = FormatMessageRow(msg);
                var row = new TableRow(star, clip, from, subject, date) { Tag = msg };
                _messageTable.AddRow(row);
            }
            return;
        }

        // Same folder — use in-place diff

        // Track if the currently selected message gets removed
        uint? selectedUid = null;
        var selIdx = _messageTable.SelectedRowIndex;
        if (selIdx >= 0 && selIdx < _messageTable.RowCount)
        {
            var selRow = _messageTable.GetRow(selIdx);
            if (selRow.Tag is MailMessage selMsg)
                selectedUid = selMsg.Uid;
        }

        // Build set of incoming UIDs
        var incomingUids = new HashSet<uint>(messages.Select(m => m.Uid));
        var selectedWasRemoved = selectedUid.HasValue && !incomingUids.Contains(selectedUid.Value);

        // Remove rows not in incoming (reverse order to keep indices stable)
        for (var i = _messageTable.RowCount - 1; i >= 0; i--)
        {
            var row = _messageTable.GetRow(i);
            if (row.Tag is MailMessage m && !incomingUids.Contains(m.Uid))
                _messageTable.RemoveRow(i);
        }

        // Build lookup of existing rows by UID → current index
        var existing = new Dictionary<uint, int>();
        for (var i = 0; i < _messageTable.RowCount; i++)
        {
            var row = _messageTable.GetRow(i);
            if (row.Tag is MailMessage m)
                existing[m.Uid] = i;
        }

        // Walk desired order: update existing, insert new
        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            var (star, clip, from, subject, date) = FormatMessageRow(msg);

            if (existing.TryGetValue(msg.Uid, out var rowIdx))
            {
                // Update in-place
                _messageTable.UpdateCell(rowIdx, 0, star);
                _messageTable.UpdateCell(rowIdx, 1, clip);
                _messageTable.UpdateCell(rowIdx, 2, from);
                _messageTable.UpdateCell(rowIdx, 3, subject);
                _messageTable.UpdateCell(rowIdx, 4, date);
                _messageTable.GetRow(rowIdx).Tag = msg;
            }
            else
            {
                // Insert at correct position
                var row = new TableRow(star, clip, from, subject, date) { Tag = msg };
                _messageTable.InsertRow(i, row);

                // Rebuild lookup — indices after insertion shifted
                existing.Clear();
                for (var j = 0; j < _messageTable.RowCount; j++)
                {
                    var r = _messageTable.GetRow(j);
                    if (r.Tag is MailMessage m)
                        existing[m.Uid] = j;
                }
            }
        }

        // If the previously selected message was removed, update the reading pane
        if (selectedWasRemoved)
        {
            var nextMsg = GetSelectedMessage();
            if (nextMsg != null)
            {
                ShowMessagePreview(nextMsg);
                UpdatePreviewHeader(nextMsg);
            }
            else
            {
                ClearReadingPane();
                UpdatePreviewHeader();
            }
            UpdateBottomBar();
            UpdateToolbar();
        }
    }

    private static (string star, string clip, string from, string subject, string date)
        FormatMessageRow(MailMessage msg)
    {
        var star = msg.IsFlagged ? "[yellow]\u2605[/]" : "[grey35]\u2606[/]";
        var clip = msg.HasAttachments ? "[grey70]\U0001f4ce[/]" : "";
        var from = msg.IsRead
            ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]"
            : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]";
        var subject = msg.IsRead
            ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]"
            : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]";
        var date = FormatDate(msg.Date);
        return (star, clip, from, subject, date);
    }

    private void OnMessageSelected(object? sender, int rowIndex)
    {
        if (_messageTable == null || rowIndex < 0) return;
        var row = _messageTable.GetRow(rowIndex);
        if (row?.Tag is not MailMessage msg) return;

        // Always show preview for cursor message (checkboxes managed by MultiSelectionChanged event)
        ShowMessagePreview(msg);
        UpdatePreviewHeader(msg);
        UpdateBottomBar();
        UpdateToolbar();
        RetainMessageListFocus();

        _messageListCoordinator.SelectMessage(msg);

        // Cancel any in-flight body fetch from a previous selection
        var oldCts = _bodyFetchCts;
        _bodyFetchCts = null;
        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
        oldCts?.Dispose();

        // Debounce body fetch — wait 150ms before starting connection
        // Prevents connection storm when scrolling through messages rapidly
        _bodyFetchDebounce?.Dispose();

        var needsFetch = !msg.BodyFetched || (msg.HasAttachments && msg.Attachments == null);
        if (needsFetch)
        {
            var capturedMsg = msg;
            _bodyFetchDebounce = new System.Threading.Timer(_ =>
            {
                // Verify this message is still selected after debounce
                if (_messageListCoordinator.SelectedMessage?.Uid != capturedMsg.Uid) return;

                var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _bodyFetchCts = fetchCts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _messageListCoordinator.FetchAndShowBodyAsync(capturedMsg, fetchCts.Token);

                        EnqueueUiAction(() =>
                        {
                            if (_messageListCoordinator.SelectedMessage?.Uid == capturedMsg.Uid)
                            {
                                ShowMessagePreview(capturedMsg);
                                TriggerReadingPaneFadeIn();
                                RetainMessageListFocus();
                            }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        ImapLogger.Debug($"Body fetch cancelled for uid={capturedMsg.Uid} (superseded by newer selection)");
                    }
                    catch (Exception ex)
                    {
                        ImapLogger.Error($"Body fetch failed for uid={capturedMsg.Uid}: {ex.GetType().Name}: {ex.Message}", ex);
                        if (_messageListCoordinator.SelectedMessage?.Uid == capturedMsg.Uid)
                            EnqueueUiAction(() => ShowError($"Failed to load message: {ex.Message}"));
                    }
                }, fetchCts.Token);
            }, null, 150, Timeout.Infinite);
        }
    }

    private void OnMessageActivated(object? sender, int rowIndex)
    {
        OnMessageSelected(sender, rowIndex);
    }

    private MailMessage? GetSelectedMessage()
    {
        if (_messageTable == null) return null;
        var idx = _messageTable.SelectedRowIndex;
        if (idx < 0) return null;
        var row = _messageTable.GetRow(idx);
        return row?.Tag as MailMessage;
    }

    public List<MailMessage> GetDisplayedMessages()
    {
        if (_messageTable == null) return [];
        var messages = new List<MailMessage>();
        for (var i = 0; i < _messageTable.RowCount; i++)
        {
            var row = _messageTable.GetRow(i);
            if (row.Tag is MailMessage msg)
                messages.Add(msg);
        }
        return messages;
    }

    private List<MailMessage> GetCheckedMessages()
    {
        if (_messageTable == null) return [];
        return _messageTable.GetSelectedRows()
            .Where(r => r.Tag is MailMessage)
            .Select(r => (MailMessage)r.Tag!)
            .ToList();
    }

    private int GetCheckedCount() => _messageTable?.GetSelectedRows().Count ?? 0;

    private void ClearSelection()
    {
        _messageTable?.ClearSelection();
        UpdateToolbar();
        UpdateBottomBar();
        // Restore normal header
        var msg = GetSelectedMessage();
        if (msg != null)
            UpdatePreviewHeader(msg);
        else
            SetRightPanelHeader("[grey70]Messages[/]", showSyncAction: !_isSearchActive);
    }

    public void RefreshCurrentMessageList()
    {
        if (_isSearchActive) return;
        _messageListCoordinator.RefreshMessageList();
    }

    /// <summary>
    /// Refreshes the message list only if the given folder is currently selected.
    /// </summary>
    public void RefreshCurrentMessageListIfFolder(int folderId)
    {
        if (_isSearchActive) return;

        // Check if this folder is the current folder
        if (_messageListCoordinator.CurrentFolder?.Id == folderId)
        {
            // Check if the currently previewed message was removed
            var selectedMsg = _messageListCoordinator.SelectedMessage;
            if (selectedMsg != null)
            {
                var cachedUids = _cacheService.GetCachedUids(folderId);
                if (!cachedUids.Contains(selectedMsg.Uid))
                {
                    // Selected message was deleted on server — clear preview
                    _messageListCoordinator.SelectMessage(null!);
                    ClearReadingPane();
                    UpdatePreviewHeader();
                }
                else
                {
                    // Refresh the preview in case flags changed
                    var msgs = _cacheService.GetMessages(folderId);
                    var updated = msgs.FirstOrDefault(m => m.Uid == selectedMsg.Uid);
                    if (updated != null && (updated.IsRead != selectedMsg.IsRead || updated.IsFlagged != selectedMsg.IsFlagged))
                    {
                        _messageListCoordinator.SelectMessage(updated);
                        ShowMessagePreview(updated);
                    }
                }
            }

            _messageListCoordinator.RefreshMessageList();
            RetainMessageListFocus();
            return;
        }

        // Check if this folder is part of an aggregated view
        if (_aggregatedFolderIds != null && _aggregatedFolderIds.Contains(folderId))
        {
            RefreshAggregatedView();
            RetainMessageListFocus();
        }
    }

    private void RefreshAggregatedView()
    {
        if (_aggregatedFolderIds == null) return;
        var allMessages = new List<MailMessage>();
        foreach (var fId in _aggregatedFolderIds)
            allMessages.AddRange(_cacheService.GetMessages(fId));
        allMessages.Sort((a, b) => b.Date.CompareTo(a.Date));
        PopulateMessageList(allMessages);
    }

    private static string FormatDate(DateTime date)
    {
        var now = DateTime.Now;
        var localDate = date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
        if (localDate.Date == now.Date)
            return localDate.ToString("h:mm tt");
        if (localDate.Year == now.Year)
            return localDate.ToString("MMM d");
        return localDate.ToString("MMM d, yyyy");
    }

    /// <summary>
    /// Re-focuses the message table if it was the last focused control.
    /// Prevents body fetch / mark-as-read from stealing focus to reading pane.
    /// </summary>
    public void RetainMessageListFocus()
    {
        if (_messageTable == null || _mainWindow == null) return;
        var focused = _mainWindow.FocusManager?.FocusedControl;
        // If nothing is focused, or a non-interactive control got focus, restore to table
        if (focused == null || focused == _readingPane || focused is MarkupControl || focused is ScrollablePanelControl)
            _mainWindow.FocusManager?.SetFocus(_messageTable, FocusReason.Programmatic);
    }
}
