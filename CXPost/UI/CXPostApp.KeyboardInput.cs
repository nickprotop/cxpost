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
    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift);

        // F2: toggle folder tree visibility
        if (e.KeyInfo.Key == KeyBindings.ToggleTree)
        {
            ToggleFolderTree();
            e.Handled = true;
            return;
        }
        // F3: toggle preview panel visibility
        if (e.KeyInfo.Key == KeyBindings.TogglePreview)
        {
            TogglePreview();
            e.Handled = true;
            return;
        }
        // F4: toggle read mode
        if (e.KeyInfo.Key == KeyBindings.ReadMode)
        {
            if (_layoutModeManager.IsReadMode)
                ExitReadMode();
            else
                EnterReadMode();
            e.Handled = true;
            return;
        }
        // Escape exits read mode
        if (e.KeyInfo.Key == ConsoleKey.Escape && _layoutModeManager.IsReadMode)
        {
            ExitReadMode();
            e.Handled = true;
            return;
        }
        // Ctrl+B: toggle message strip in read mode
        if (ctrl && e.KeyInfo.Key == ConsoleKey.B && _layoutModeManager.IsReadMode)
        {
            ToggleReadStrip();
            e.Handled = true;
            return;
        }

        if (e.KeyInfo.Key == ConsoleKey.Escape && GetCheckedCount() > 0)
        {
            ClearSelection();
            ClearReadingPane();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Escape && _isSearchActive)
        {
            ClearSearch();
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ComposeNew)
        {
            _ = Task.Run(async () =>
            {
                var account = GetAccountForMessage(null);
                var dialog = new ComposeDialog(_contactsService, _config.Accounts,
                    defaultAccountId: account?.Id,
                    cc: account?.DefaultCc ?? "");
                var result = await dialog.ShowAsync(_ws);
                if (result != null)
                    await SendWithProgressAsync(result);
            });
            e.Handled = true;
        }
        else if (ctrl && shift && e.KeyInfo.Key == KeyBindings.Reply)
        {
            // Reply all
            var msg = GetSelectedMessage();
            var account = GetAccountForMessage(msg);
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareReply(account, msg, replyAll: true);
                _ = Task.Run(async () =>
                {
                    var dialog = new ComposeDialog(_contactsService, _config.Accounts,
                        defaultAccountId: account.Id, to: to, subject: subject, body: body,
                        originalAttachments: msg.Attachments);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                        await SendReplyWithAttachmentsAsync(result, account, msg);
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Reply)
        {
            var msg = GetSelectedMessage();
            var account = GetAccountForMessage(msg);
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareReply(account, msg, replyAll: false);
                _ = Task.Run(async () =>
                {
                    var dialog = new ComposeDialog(_contactsService, _config.Accounts,
                        defaultAccountId: account.Id, to: to, subject: subject, body: body,
                        originalAttachments: msg.Attachments);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                        await SendReplyWithAttachmentsAsync(result, account, msg);
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Forward)
        {
            var checkedMessages = GetCheckedMessages();
            if (checkedMessages.Count > 1)
            {
                // Bulk forward
                var account = GetAccountForMessage(checkedMessages[0]);
                if (account != null)
                {
                    var progressId = "fwd-progress";
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var dialog = new BulkForwardDialog(
                                checkedMessages,
                                _config.Accounts,
                                defaultAccountId: account.Id,
                                composeCoordinator: _composeCoordinator,
                                contacts: _contactsService,
                                onProgress: msg => EnqueueUiAction(() => ReplaceMessage(progressId, msg)),
                                onSuccess: msg => EnqueueUiAction(() =>
                                {
                                    DismissMessage(progressId);
                                    ShowSuccess(msg);
                                    ClearSelection();
                                }),
                                onError: msg => EnqueueUiAction(() =>
                                {
                                    DismissMessage(progressId);
                                    ShowError(msg);
                                }),
                                ct: _cts.Token);
                            await dialog.ShowAsync(_ws);
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Forward failed: {ex.Message}"));
                        }
                    });
                }
            }
            else
            {
                // Single forward (enhanced with attachment toggle)
                var msg = GetSelectedMessage();
                var account = GetAccountForMessage(msg);
                if (msg != null && account != null)
                {
                    var (to, subject, body) = _composeCoordinator.PrepareForward(account, msg);
                    _ = Task.Run(async () =>
                    {
                        var dialog = new ComposeDialog(_contactsService, _config.Accounts,
                            defaultAccountId: account.Id, to: to, subject: subject, body: body,
                            isForwardMode: true,
                            originalAttachments: msg.Attachments);
                        var result = await dialog.ShowAsync(_ws);
                        if (result != null)
                        {
                            if (result.IncludeOriginalAttachments && msg.HasAttachments
                                && msg.Attachments != null && msg.Attachments.Count > 0)
                            {
                                var tempDir = Path.Combine(Path.GetTempPath(), $"cxpost-fwd-{Guid.NewGuid():N}");
                                var progressId = "fwd-progress";
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
                                                ShowInfo("Forward cancelled.");
                                            });
                                            return;
                                        }
                                    }

                                    var allPaths = new List<string>(result.AttachmentPaths);
                                    allPaths.AddRange(paths);

                                    EnqueueUiAction(() => ReplaceMessage(progressId, "Sending forwarded message..."));
                                    await _composeCoordinator.SendAsync(
                                        account, result.FromName, result.To, result.Cc,
                                        result.Subject, result.Body, allPaths, _cts.Token);

                                    EnqueueUiAction(() =>
                                    {
                                        DismissMessage(progressId);
                                        ShowSuccess($"Message forwarded to {result.To}");
                                    });
                                }
                                catch (Exception ex)
                                {
                                    EnqueueUiAction(() =>
                                    {
                                        DismissMessage(progressId);
                                        ShowError($"Forward failed: {ex.Message}");
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
                    });
                }
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Search)
        {
            _ = Task.Run(async () =>
            {
                List<string> recentCopy;
                lock (_searchLock)
                {
                    recentCopy = new List<string>(_recentSearches);
                }
                var dialog = new SearchDialog(recentCopy);
                var query = await dialog.ShowAsync(_ws);
                if (query != null)
                {
                    // Track recent searches
                    lock (_searchLock)
                    {
                        _recentSearches.Remove(query);
                        _recentSearches.Insert(0, query);
                        if (_recentSearches.Count > 5) _recentSearches.RemoveAt(5);
                    }

                    _isSearchActive = true;
                    _activeSearchQuery = query;

                    // Determine search folders
                    var searchFolders = new List<MailFolder>();
                    if (_isAggregatedView && _aggregatedFolders.Count > 0)
                    {
                        foreach (var folders in _aggregatedFolders.Values)
                            searchFolders.AddRange(folders);
                    }
                    else if (_messageListCoordinator.CurrentFolder != null)
                    {
                        searchFolders.Add(_messageListCoordinator.CurrentFolder);
                    }

                    if (searchFolders.Count == 0) return;

                    // Local search — instant results from cache
                    var localResults = new List<MailMessage>();
                    var lowerQuery = query.ToLowerInvariant();
                    foreach (var folder in searchFolders)
                    {
                        var msgs = _cacheService.GetMessages(folder.Id);
                        localResults.AddRange(msgs.Where(m =>
                            (m.Subject?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (m.FromName?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (m.FromAddress?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false)));
                    }
                    localResults.Sort((a, b) => b.Date.CompareTo(a.Date));

                    var folderName = searchFolders.Count == 1
                        ? searchFolders[0].DisplayName
                        : "All Folders";

                    EnqueueUiAction(() =>
                    {
                        PopulateMessageList(localResults);
                        SetRightPanelHeader(
                            $"[grey70]Search:[/] [white]{MarkupParser.Escape(query)}[/] [grey50]({localResults.Count} results in {MarkupParser.Escape(folderName)})[/]",
                            "Clear");
                        ShowInfo($"Searching server for \"{query}\"...");
                    });

                    // Server search — refine with IMAP results
                    try
                    {
                        var serverResults = new List<MailMessage>();
                        foreach (var folder in searchFolders)
                        {
                            var results = await _searchCoordinator.SearchAsync(folder, query, _cts.Token);
                            serverResults.AddRange(results);
                        }
                        serverResults.Sort((a, b) => b.Date.CompareTo(a.Date));

                        if (_isSearchActive && _activeSearchQuery == query)
                        {
                            EnqueueUiAction(() =>
                            {
                                PopulateMessageList(serverResults);
                                SetRightPanelHeader(
                                    $"[grey70]Search:[/] [white]{MarkupParser.Escape(query)}[/] [grey50]({serverResults.Count} results in {MarkupParser.Escape(folderName)})[/]",
                                    "Clear");
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Server search failed: {ex.Message}"));
                    }
                }
            });
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Delete)
        {
            var folder = _messageListCoordinator.CurrentFolder;
            var checkedMsgs = GetCheckedMessages();

            if (checkedMsgs.Count > 0 && folder != null)
            {
                // Bulk delete with confirmation
                var count = checkedMsgs.Count;
                _ = Task.Run(async () =>
                {
                    var dialog = new ConfirmDialog(
                        "Delete Messages",
                        $"Delete {count} message{(count != 1 ? "s" : "")}?");
                    var confirmed = await dialog.ShowAsync(_ws);
                    if (confirmed)
                    {
                        EnqueueUiAction(() =>
                        {
                            // Animate checked rows fading out before removal
                            if (_messageTable != null)
                            {
                                var checkedIndices = new List<int>();
                                for (var i = 0; i < _messageTable.RowCount; i++)
                                {
                                    var row = _messageTable.GetRow(i);
                                    if (row.Tag is MailMessage m && checkedMsgs.Any(cm => cm.Uid == m.Uid))
                                        checkedIndices.Add(i);
                                }
                                if (checkedIndices.Count > 0)
                                    _messageTable.AnimateRowsRemoval(checkedIndices.ToArray(), TimeSpan.FromMilliseconds(300));
                            }

                            _messageListCoordinator.DeleteMultipleOptimistic(checkedMsgs, folder, _cts.Token);
                            ClearSelection();
                            ClearReadingPane();
                            UpdateBottomBar();
                            UpdateToolbar();
                        });
                    }
                });
            }
            else
            {
                var msg = GetSelectedMessage();
                if (msg != null && folder != null)
                {
                    var account = GetAccountForMessage(msg) ?? GetCurrentAccount();
                    var trash = account != null ? FolderResolver.GetTrash(account, _cacheService) : null;
                    var isInTrash = trash != null && folder.Id == trash.Id;
                    var noTrash = trash == null;

                    if (isInTrash || noTrash)
                    {
                        _ = Task.Run(async () =>
                        {
                            var dialog = new ConfirmDialog(
                                "Permanently Delete",
                                "This message will be permanently deleted. This cannot be undone.");
                            var confirmed = await dialog.ShowAsync(_ws);
                            if (confirmed)
                            {
                                EnqueueUiAction(() =>
                                {
                                    // Animate row fading out before permanent deletion
                                    var rowIdx = _messageTable?.SelectedRowIndex ?? -1;
                                    if (rowIdx >= 0)
                                        _messageTable?.AnimateRowRemoval(rowIdx, TimeSpan.FromMilliseconds(300));

                                    _cacheService.DeleteMessage(folder.Id, msg.Uid);
                                    _messageListCoordinator.RefreshMessageList();
                                    var nextMsg = GetSelectedMessage();
                                    if (nextMsg != null) ShowMessagePreview(nextMsg);
                                    else ClearReadingPane();
                                    UpdateBottomBar();
                                    UpdateToolbar();
                                });
                                try
                                {
                                    using var imap = await _imapFactory.CreateConnectionAsync(account!, _cts.Token);
                                    await imap.DeleteMessageAsync(folder.Path, msg.Uid, _cts.Token);
                                }
                                catch (Exception ex)
                                {
                                    EnqueueUiAction(() => ShowError($"Delete failed: {ex.Message}"));
                                }
                            }
                        });
                    }
                    else
                    {
                        // Animate row fading out before soft delete (move to trash)
                        var rowIdx = _messageTable?.SelectedRowIndex ?? -1;
                        if (rowIdx >= 0)
                            _messageTable?.AnimateRowRemoval(rowIdx, TimeSpan.FromMilliseconds(300));

                        _messageListCoordinator.DeleteMessageOptimistic(msg, folder, _cts.Token);
                        var nextMsg = GetSelectedMessage();
                        if (nextMsg != null) ShowMessagePreview(nextMsg);
                        else ClearReadingPane();
                        UpdateBottomBar();
                        UpdateToolbar();
                    }
                }
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleFlag)
        {
            var folder = _messageListCoordinator.CurrentFolder;
            var checkedMsgs = GetCheckedMessages();
            if (checkedMsgs.Count > 0 && folder != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var willFlag = checkedMsgs.Any(m => !m.IsFlagged);
                        var progressLabel = willFlag ? "Flagging" : "Unflagging";
                        var progressId = "flag-progress";
                        EnqueueUiAction(() => ReplaceMessage(progressId, $"{progressLabel} {checkedMsgs.Count} messages..."));
                        await _messageListCoordinator.ToggleFlagMultipleAsync(checkedMsgs, folder, _cts.Token);
                        var label = willFlag ? "flagged" : "unflagged";
                        EnqueueUiAction(() =>
                        {
                            DismissMessage(progressId);
                            ShowSuccess($"{checkedMsgs.Count} messages {label}");
                            if (_isFlaggedFilterActive) RefreshFlaggedHeader();
                        });
                    }
                    catch (Exception ex) { EnqueueUiAction(() => ShowError($"Bulk flag failed: {ex.Message}")); }
                });
            }
            else
            {
                var msg = GetSelectedMessage();
                if (msg != null && folder != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var willFlag = !msg.IsFlagged;
                            var progressId = "flag-progress";
                            var progressLabel = willFlag ? "Flagging" : "Unflagging";
                            EnqueueUiAction(() => ReplaceMessage(progressId, $"{progressLabel}..."));
                            await _messageListCoordinator.ToggleFlagAsync(msg, folder, _cts.Token);
                            var label = willFlag ? "Flagged" : "Unflagged";
                            EnqueueUiAction(() =>
                            {
                                DismissMessage(progressId);
                                ShowSuccess(label);
                                if (_isFlaggedFilterActive) RefreshFlaggedHeader();
                                var rowIdx = _messageTable?.SelectedRowIndex ?? -1;
                                if (rowIdx >= 0)
                                    _messageTable?.FlashCell(rowIdx, 0, Color.Yellow, TimeSpan.FromMilliseconds(200));
                            });
                        }
                        catch (Exception ex) { EnqueueUiAction(() => ShowError($"Toggle flag failed: {ex.Message}")); }
                    });
                }
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleRead)
        {
            var folder = _messageListCoordinator.CurrentFolder;
            var checkedMsgs = GetCheckedMessages();
            if (checkedMsgs.Count > 0 && folder != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var willRead = checkedMsgs.Any(m => !m.IsRead);
                        var progressId = "read-progress";
                        var progressLabel = willRead ? "Marking as read" : "Marking as unread";
                        EnqueueUiAction(() => ReplaceMessage(progressId, $"{progressLabel} {checkedMsgs.Count} messages..."));
                        await _messageListCoordinator.ToggleReadMultipleAsync(checkedMsgs, folder, _cts.Token);
                        var label = willRead ? "read" : "unread";
                        EnqueueUiAction(() =>
                        {
                            DismissMessage(progressId);
                            ShowSuccess($"{checkedMsgs.Count} messages marked {label}");
                        });
                    }
                    catch (Exception ex) { EnqueueUiAction(() => ShowError($"Bulk read toggle failed: {ex.Message}")); }
                });
            }
            else
            {
                var msg = GetSelectedMessage();
                if (msg != null && folder != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var willRead = !msg.IsRead;
                            var progressId = "read-progress";
                            var progressLabel = willRead ? "Marking as read" : "Marking as unread";
                            EnqueueUiAction(() => ReplaceMessage(progressId, $"{progressLabel}..."));
                            await _messageListCoordinator.ToggleReadAsync(msg, folder, _cts.Token);
                            var label = willRead ? "Marked as read" : "Marked as unread";
                            EnqueueUiAction(() =>
                            {
                                DismissMessage(progressId);
                                ShowSuccess(label);
                                var rowIdx = _messageTable?.SelectedRowIndex ?? -1;
                                if (rowIdx >= 0)
                                {
                                    var flashColor = willRead ? Color.Grey50 : Color.White;
                                    _messageTable?.FlashRow(rowIdx, flashColor, TimeSpan.FromMilliseconds(250));
                                }
                            });
                        }
                        catch (Exception ex) { EnqueueUiAction(() => ShowError($"Toggle read failed: {ex.Message}")); }
                    });
                }
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.MoveToFolder)
        {
            var folder = _messageListCoordinator.CurrentFolder;
            var checkedMsgs = GetCheckedMessages();

            if (checkedMsgs.Count > 0 && folder != null)
            {
                var folders = _cacheService.GetFolders(folder.AccountId);
                var count = checkedMsgs.Count;
                _ = Task.Run(async () =>
                {
                    var dialog = new FolderPickerDialog(folders);
                    var dest = await dialog.ShowAsync(_ws);
                    if (dest != null)
                    {
                        try
                        {
                            EnqueueUiAction(() => ShowProgress($"Moving {count} message{(count != 1 ? "s" : "")}..."));
                            var account = GetAccountForMessage(checkedMsgs[0]) ?? GetCurrentAccount();
                            if (account != null)
                            {
                                using var imap = await _imapFactory.CreateConnectionAsync(account, _cts.Token);
                                foreach (var msg in checkedMsgs)
                                {
                                    await imap.MoveMessageAsync(folder.Path, dest.Path, msg.Uid, _cts.Token);
                                }
                            }
                            EnqueueUiAction(() =>
                            {
                                // Animate checked rows fading out before move
                                if (_messageTable != null)
                                {
                                    var checkedIndices = new List<int>();
                                    for (var i = 0; i < _messageTable.RowCount; i++)
                                    {
                                        var row = _messageTable.GetRow(i);
                                        if (row.Tag is MailMessage m && checkedMsgs.Any(cm => cm.Uid == m.Uid))
                                            checkedIndices.Add(i);
                                    }
                                    if (checkedIndices.Count > 0)
                                        _messageTable.AnimateRowsRemoval(checkedIndices.ToArray(), TimeSpan.FromMilliseconds(300));
                                }

                                foreach (var msg in checkedMsgs)
                                {
                                    _cacheService.DeleteMessage(folder.Id, msg.Uid);
                                }
                                ClearSelection();
                                ClearReadingPane();
                                _messageListCoordinator.RefreshMessageList();
                                UpdatePreviewHeader();
                                UpdateBottomBar();
                                UpdateToolbar();
                                ShowSuccess($"Moved {count} message{(count != 1 ? "s" : "")} to {dest.DisplayName}");
                            });
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Move failed: {ex.Message}"));
                        }
                    }
                });
            }
            else
            {
                var msg = GetSelectedMessage();
                if (msg != null && folder != null)
                {
                    var folders = _cacheService.GetFolders(folder.AccountId);
                    _ = Task.Run(async () =>
                    {
                        var dialog = new FolderPickerDialog(folders);
                        var dest = await dialog.ShowAsync(_ws);
                        if (dest != null)
                        {
                            try
                            {
                                var account = GetAccountForMessage(msg);
                                if (account != null)
                                {
                                    using var imap = await _imapFactory.CreateConnectionAsync(account, _cts.Token);
                                    await imap.MoveMessageAsync(folder.Path, dest.Path, msg.Uid, _cts.Token);
                                }
                                EnqueueUiAction(() =>
                                {
                                    // Animate row fading out before move
                                    var rowIdx = _messageTable?.SelectedRowIndex ?? -1;
                                    if (rowIdx >= 0)
                                        _messageTable?.AnimateRowRemoval(rowIdx, TimeSpan.FromMilliseconds(300));

                                    _cacheService.DeleteMessage(folder.Id, msg.Uid);
                                    _messageListCoordinator.RefreshMessageList();
                                    ClearReadingPane();
                                    UpdatePreviewHeader();
                                    UpdateBottomBar();
                                    UpdateToolbar();
                                    ShowSuccess($"Moved to {dest.DisplayName}");
                                });
                            }
                            catch (Exception ex)
                            {
                                EnqueueUiAction(() => ShowError($"Move failed: {ex.Message}"));
                            }
                        }
                    });
                }
            }
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.RefreshFolder && shift)
        {
            SyncActiveFolder();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Refresh && !shift)
        {
            _ = Task.Run(async () =>
            {
                foreach (var account in _config.Accounts)
                {
                    try
                    {
                        await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
                        EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Sync failed: {ex.Message}"));
                    }
                }
            });
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.A && !ctrl && !shift)
        {
            var msg = GetSelectedMessage();
            if (msg?.Attachments != null && msg.Attachments.Count > 1)
            {
                SaveAllAttachments(msg);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == ConsoleKey.A && ctrl && !shift)
        {
            var msg = GetSelectedMessage();
            if (msg?.Attachments != null && msg.Attachments.Count > 1)
            {
                SaveAllAttachmentsAs(msg);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key >= ConsoleKey.D1 && e.KeyInfo.Key <= ConsoleKey.D9)
        {
            var msg = GetSelectedMessage();
            var idx = (int)(e.KeyInfo.Key - ConsoleKey.D1);
            if (msg?.Attachments != null && idx < msg.Attachments.Count)
            {
                if (ctrl)
                    SaveAttachmentAs(msg, idx);
                else
                    SaveAttachmentQuick(msg, idx, msg.Attachments[idx].FileName);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == KeyBindings.SwitchLayout)
        {
            var isDashboard = _dashboardPanel?.Visible == true;
            _currentLayout = _currentLayout == "classic" ? "wide" : "classic";
            // Preserve "last" preference — only update LastLayout, not Layout
            _config.LastLayout = _currentLayout;
            if (_config.Layout != "last")
                _config.Layout = _currentLayout;
            _configService.Save(_config);
            RebuildMainGrid();

            // Re-apply current view visibility
            if (isDashboard)
                ApplyDashboardVisibility();
            else
                ShowMessageListView();

            UpdateToolbar();
            UpdateBottomBar();
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Settings)
        {
            _ = Task.Run(async () =>
            {
                var dialog = new SettingsDialog(_config, _configService, _credentialService, _cacheService, _ws);
                var changed = await dialog.ShowAsync(_ws);
                if (changed)
                {
                    _config = _configService.Load();

                    // Reset IMAP connections to pick up credential/host changes
                    foreach (var account in _config.Accounts)
                        _ = _imapFactory.ResetAllAsync(account.Id);

                    // Restart sync loops with updated accounts
                    StartBackgroundSync();

                    EnqueueUiAction(() =>
                    {
                        RefreshFolderTree();
                        ShowSuccess("Settings saved");
                    });
                }
            });
            e.Handled = true;
        }
    }

    private async Task ShowFirstRunSetupAsync()
    {
        var dialog = new AccountSettingsDialog();
        var account = await dialog.ShowAsync(_ws);
        if (account != null)
        {
            // Store the password
            var password = dialog.GetPassword();
            if (!string.IsNullOrEmpty(password))
                _credentialService.StorePassword(account.Id, password);

            // Save account to config
            _config.Accounts.Add(account);
            _configService.Save(_config);

            // Refresh UI and start sync
            EnqueueUiAction(() =>
            {
                PopulateFolderTree();
                _notificationCoordinator.NotifySendSuccess($"Account {account.Name} added");
            });

            StartBackgroundSync();
        }
    }

    private Account? GetCurrentAccount()
    {
        var folder = _messageListCoordinator.CurrentFolder;
        if (folder == null) return _config.Accounts.FirstOrDefault();
        return _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId)
            ?? _config.Accounts.FirstOrDefault();
    }

    /// <summary>
    /// Resolves the account for a specific message — uses the message's AccountId
    /// if available, falls back to folder's AccountId, then first account.
    /// </summary>
    private Account? GetAccountForMessage(MailMessage? msg)
    {
        if (msg?.AccountId != null)
            return _config.Accounts.FirstOrDefault(a => a.Id == msg.AccountId);

        // Fallback: try folder
        var folder = _messageListCoordinator.CurrentFolder;
        if (folder != null)
            return _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId);

        return _config.Accounts.FirstOrDefault();
    }
}
