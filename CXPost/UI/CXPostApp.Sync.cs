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
    private volatile bool _folderSyncInProgress;

    private void StartBackgroundSync()
    {
        // Stop any existing sync loops
        StopAllSyncLoops();

        foreach (var account in _config.Accounts)
        {
            var loopCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _syncLoopCts[account.Id] = loopCts;
            var capturedAccount = account; // capture for closure

            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncCoordinator.SyncAccountAsync(capturedAccount, loopCts.Token);
                    EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    EnqueueUiAction(() =>
                    {
                        _statusBar.UpdateConnectionStatus(0, false);
                        ShowError($"Initial sync failed for {capturedAccount.Name}: {ex.Message}");
                    });
                }

                while (!loopCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var syncInterval = capturedAccount.SyncIntervalSeconds > 0
                            ? capturedAccount.SyncIntervalSeconds
                            : _config.SyncIntervalSeconds;
                        await Task.Delay(TimeSpan.FromSeconds(syncInterval), loopCts.Token);
                        await _syncCoordinator.SyncAccountAsync(capturedAccount, loopCts.Token);
                        EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() =>
                        {
                            _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), false);
                            ShowError($"Sync failed: {ex.Message}");
                        });
                    }
                }
            }, loopCts.Token);
        }
    }

    private void StopAllSyncLoops()
    {
        foreach (var (id, cts) in _syncLoopCts)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _syncLoopCts.Clear();
    }

    private void SyncActiveFolder()
    {
        // Guards: no sync during search, dashboard, or bulk selection
        if (_isSearchActive) return;
        if (_dashboardPanel?.Visible == true) return;
        if (GetCheckedCount() > 0) return;
        if (_folderSyncInProgress) return;

        _folderSyncInProgress = true;

        if (_isAggregatedView && _aggregatedFolderIds != null)
        {
            // Aggregated view: sync this folder type across all accounts sequentially
            var folders = _aggregatedFolderIds
                .Select(id => FindFolderById(id))
                .Where(f => f != null)
                .Select(f => (folder: f!, account: _config.Accounts.FirstOrDefault(a => a.Id == f!.AccountId)))
                .Where(x => x.account != null)
                .Select(x => (x.folder, account: x.account!))
                .ToList();
            if (folders.Count == 0) { _folderSyncInProgress = false; return; }

            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var (folder, account) in folders)
                        await _syncCoordinator.SyncSingleFolderAsync(account, folder, _cts.Token);
                    EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                }
                catch (Exception ex)
                {
                    EnqueueUiAction(() => ShowError($"Sync failed: {ex.Message}"));
                }
                finally
                {
                    _folderSyncInProgress = false;
                }
            });
        }
        else
        {
            // Single folder view
            var folder = _messageListCoordinator.CurrentFolder;
            if (folder == null) { _folderSyncInProgress = false; return; }
            var account = _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
            if (account == null) { _folderSyncInProgress = false; return; }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncCoordinator.SyncSingleFolderAsync(account, folder, _cts.Token);
                    EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                }
                catch (Exception ex)
                {
                    EnqueueUiAction(() => ShowError($"Sync failed: {ex.Message}"));
                }
                finally
                {
                    _folderSyncInProgress = false;
                }
            });
        }
    }

    private int GetTotalUnreadCount()
    {
        var count = 0;
        foreach (var account in _config.Accounts)
        {
            var folders = _cacheService.GetFolders(account.Id);
            foreach (var folder in folders)
                count += _cacheService.GetMessages(folder.Id).Count(m => !m.IsRead);
        }
        return count;
    }

    private void UpdateClockDisplay()
    {
        if (_topStatusRight == null) return;

        var time = DateTime.Now.ToString("h:mm tt");
        var unreadCount = GetTotalUnreadCount();
        var connected = _config.Accounts.Count > 0;

        var status = connected
            ? $"[{ColorScheme.FlaggedMarkup}]{unreadCount} unread[/] [grey50]|[/] [{ColorScheme.SuccessMarkup}]\u25cf Connected[/] [grey50]|[/] [grey70]{time}[/]"
            : $"[{ColorScheme.ErrorMarkup}]\u25cf Offline[/] [grey50]|[/] [grey70]{time}[/]";

        _topStatusRight.SetContent([status]);
    }

    private void UpdateSyncSpinner()
    {
        if (_folderTree == null) return;
        // Only invalidate the tree to trigger a repaint — the spinner frame
        // is read during render from _spinnerIndex
        _mainWindow?.Invalidate(false);
    }
}
