using System.Collections.Concurrent;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class MailSyncCoordinator
{
    private readonly IImapService _imap;
    private readonly ICacheService _cache;
    private readonly ThreadingService _threading;
    private readonly Lazy<CXPostApp> _app;
    private readonly NotificationCoordinator _notifications;
    private readonly ConcurrentDictionary<string, bool> _syncingAccounts = new();

    public MailSyncCoordinator(
        IImapService imap,
        ICacheService cache,
        ThreadingService threading,
        Lazy<CXPostApp> app,
        NotificationCoordinator notifications)
    {
        _imap = imap;
        _cache = cache;
        _threading = threading;
        _app = app;
        _notifications = notifications;
    }

    public async Task SyncAccountAsync(Account account, CancellationToken ct)
    {
        if (!_syncingAccounts.TryAdd(account.Id, true))
            return; // Already syncing

        var syncMsgId = $"sync-{account.Id}";

        try
        {
            // Progress: connecting
            _app.Value.EnqueueUiAction(() =>
                _app.Value.ReplaceMessage(syncMsgId,
                    $"Connecting to {account.ImapHost}:{account.ImapPort}..."));

            if (!_imap.IsConnected)
                await _imap.ConnectAsync(account, ct);

            // Progress: fetching folder list
            _app.Value.EnqueueUiAction(() =>
                _app.Value.ReplaceMessage(syncMsgId,
                    $"{account.Name}: Fetching folder list..."));

            var folders = await _imap.GetFoldersAsync(ct);
            _cache.SyncFolders(account.Id, folders);

            // Refresh tree immediately so folders appear
            _app.Value.EnqueueUiAction(() => _app.Value.RefreshFolderTree());

            // Progress: syncing each folder
            var cachedFolders = _cache.GetFolders(account.Id);
            var totalMessages = 0;
            for (var i = 0; i < cachedFolders.Count; i++)
            {
                var folder = cachedFolders[i];
                var progress = i + 1;
                var total = cachedFolders.Count;
                _app.Value.EnqueueUiAction(() =>
                    _app.Value.ReplaceMessage(syncMsgId,
                        $"{account.Name}: Syncing {folder.DisplayName} ({progress}/{total})..."));

                var beforeCount = _cache.GetCachedUids(folder.Id).Count;
                await SyncFolderAsync(account, folder, ct);
                var afterCount = _cache.GetCachedUids(folder.Id).Count;
                totalMessages += afterCount - beforeCount;
            }

            // Final: refresh, dismiss progress bar, show toast notification
            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.RefreshFolderTree();
                _app.Value.DismissMessage(syncMsgId);
                _notifications.NotifySyncComplete(account.Name, totalMessages);
            });
        }
        catch (Exception ex)
        {
            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.DismissMessage(syncMsgId);
                _notifications.NotifyError("Sync Failed", $"{account.Name}: {ex.Message}");
            });
        }
        finally
        {
            _syncingAccounts.TryRemove(account.Id, out _);
        }
    }

    public async Task SyncFolderAsync(Account account, MailFolder folder, CancellationToken ct)
    {
        // Check UIDVALIDITY
        var serverValidity = await _imap.GetUidValidityAsync(folder.Path, ct);
        if (folder.UidValidity != 0 && folder.UidValidity != serverValidity)
        {
            // UIDVALIDITY changed — purge and resync
            _cache.PurgeFolder(folder.Id);
        }

        // Delta sync: find new UIDs
        var serverUids = await _imap.GetUidsAsync(folder.Path, ct);
        var localUids = _cache.GetCachedUids(folder.Id);

        var newUids = serverUids.Except(localUids).ToHashSet();
        var removedUids = localUids.Except(serverUids).ToHashSet();

        // Remove deleted messages
        foreach (var uid in removedUids)
            _cache.DeleteMessage(folder.Id, uid);

        // Fetch new headers
        if (newUids.Count > 0)
        {
            var minUid = newUids.Min();
            var headers = await _imap.FetchHeadersAsync(folder.Path, minUid, ct);
            var newHeaders = headers.Where(h => newUids.Contains(h.Uid)).ToList();

            // Assign thread IDs
            var allMessages = _cache.GetMessages(folder.Id);
            allMessages.AddRange(newHeaders);
            _threading.AssignThreadIds(allMessages);

            _cache.SyncHeaders(folder.Id, newHeaders);
        }
    }

    public async Task FetchBodyAsync(MailFolder folder, MailMessage message, CancellationToken ct)
    {
        if (message.BodyFetched) return;

        var body = await _imap.FetchBodyAsync(folder.Path, message.Uid, ct);
        if (body != null)
        {
            _cache.StoreBody(folder.Id, message.Uid, body);
            message.BodyPlain = body;
            message.BodyFetched = true;
        }
    }

    public async Task StartIdleAsync(Account account, string folderPath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_imap.IsConnected)
                    await _imap.ConnectAsync(account, ct);

                await _imap.IdleAsync(folderPath, () =>
                {
                    // New message detected — queue sync
                    var folder = _cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == folderPath);
                    if (folder != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await SyncFolderAsync(account, folder, ct); }
                            catch { /* logged elsewhere */ }
                        }, ct);
                    }
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Reconnect with backoff
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
