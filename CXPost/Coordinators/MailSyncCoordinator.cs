using System.Collections.Concurrent;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class MailSyncCoordinator
{
    private readonly ImapConnectionFactory _imapFactory;
    private readonly ICacheService _cache;
    private readonly IConfigService _configService;
    private readonly ThreadingService _threading;
    private readonly Lazy<CXPostApp> _app;
    private readonly NotificationCoordinator _notifications;
    private readonly ConcurrentDictionary<string, bool> _syncingAccounts = new();

    public MailSyncCoordinator(
        ImapConnectionFactory imapFactory,
        ICacheService cache,
        IConfigService configService,
        ThreadingService threading,
        Lazy<CXPostApp> app,
        NotificationCoordinator notifications)
    {
        _imapFactory = imapFactory;
        _cache = cache;
        _configService = configService;
        _threading = threading;
        _app = app;
        _notifications = notifications;
    }

    public async Task SyncAccountAsync(Account account, CancellationToken ct)
    {
        if (!_syncingAccounts.TryAdd(account.Id, true))
            return; // Already syncing

        var syncMsgId = $"sync-{account.Id}";
        var imap = _imapFactory.GetConnection(account);
        var imapLock = _imapFactory.GetLock(account.Id);

        try
        {
            _app.Value.EnqueueUiAction(() =>
                _app.Value.ReplaceMessage(syncMsgId,
                    $"Connecting to {account.ImapHost}:{account.ImapPort}..."));

            var totalMessages = 0;
            await imapLock.WaitAsync(ct);
            try
            {
                if (!imap.IsConnected)
                    await imap.ConnectAsync(account, ct);

                _app.Value.EnqueueUiAction(() =>
                    _app.Value.ReplaceMessage(syncMsgId,
                        $"{account.Name}: Fetching folder list..."));

                var folders = await imap.GetFoldersAsync(ct);
                _cache.SyncFolders(account.Id, folders);

                _app.Value.EnqueueUiAction(() => _app.Value.RefreshFolderTree());

                var cachedFolders = _cache.GetFolders(account.Id);
                for (var i = 0; i < cachedFolders.Count; i++)
                {
                    var folder = cachedFolders[i];
                    var progress = i + 1;
                    var total = cachedFolders.Count;
                    _app.Value.EnqueueUiAction(() =>
                        _app.Value.ReplaceMessage(syncMsgId,
                            $"{account.Name}: Syncing {folder.DisplayName} ({progress}/{total})..."));

                    var beforeCount = _cache.GetCachedUids(folder.Id).Count;
                    await SyncFolderAsync(account, folder, imap, ct);
                    var afterCount = _cache.GetCachedUids(folder.Id).Count;
                    totalMessages += afterCount - beforeCount;
                }
            }
            finally
            {
                imapLock.Release();
            }

            // Update last sync time and persist
            account.LastSync = DateTime.UtcNow;
            var config = _configService.Load();
            var persisted = config.Accounts.FirstOrDefault(a => a.Id == account.Id);
            if (persisted != null)
            {
                persisted.LastSync = account.LastSync;
                _configService.Save(config);
            }

            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.RefreshFolderTree();
                _app.Value.RefreshCurrentMessageList();
                _app.Value.DismissMessage(syncMsgId);
                if (account.NotificationsEnabled)
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

    private async Task SyncFolderAsync(Account account, MailFolder folder, ImapService imap, CancellationToken ct)
    {
        var serverValidity = await imap.GetUidValidityAsync(folder.Path, ct);
        if (folder.UidValidity != 0 && folder.UidValidity != serverValidity)
            _cache.PurgeFolder(folder.Id);

        var serverUids = await imap.GetUidsAsync(folder.Path, ct);

        // Apply max messages limit (#19)
        if (account.MaxMessagesPerFolder > 0 && serverUids.Count > account.MaxMessagesPerFolder)
            serverUids = serverUids.OrderByDescending(u => u).Take(account.MaxMessagesPerFolder).ToHashSet();

        var localUids = _cache.GetCachedUids(folder.Id);
        var newUids = serverUids.Except(localUids).ToHashSet();
        var removedUids = localUids.Except(serverUids).ToHashSet();

        foreach (var uid in removedUids)
            _cache.DeleteMessage(folder.Id, uid);

        if (newUids.Count > 0)
        {
            var minUid = newUids.Min();
            var headers = await imap.FetchHeadersAsync(folder.Path, minUid, ct);
            var newHeaders = headers.Where(h => newUids.Contains(h.Uid)).ToList();

            var allMessages = _cache.GetMessages(folder.Id);
            allMessages.AddRange(newHeaders);
            _threading.AssignThreadIds(allMessages);

            _cache.SyncHeaders(folder.Id, newHeaders);
        }
    }

    public async Task FetchBodyAsync(MailFolder folder, MailMessage message, CancellationToken ct)
    {
        // If body is cached but attachments aren't populated yet, still need to fetch
        if (message.BodyFetched && (message.Attachments != null || !message.HasAttachments))
            return;

        var account = _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
        if (account == null) return;
        var imap = _imapFactory.GetConnection(account);
        var (body, attachments) = await imap.FetchBodyAsync(folder.Path, message.Uid, ct);

        if (!message.BodyFetched && body != null)
        {
            _cache.StoreBody(folder.Id, message.Uid, body);
            message.BodyPlain = body;
            message.BodyFetched = true;
        }

        message.Attachments = attachments.Count > 0 ? attachments : null;
    }

    public async Task StartIdleAsync(Account account, string folderPath, CancellationToken ct)
    {
        // IDLE needs a dedicated connection — not shared with sync
        var idleImap = new ImapService(_imapFactory.Credentials);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!idleImap.IsConnected)
                    await idleImap.ConnectAsync(account, ct);

                await idleImap.IdleAsync(folderPath, () =>
                {
                    var folder = _cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == folderPath);
                    if (folder != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            var imap = _imapFactory.GetConnection(account);
                            var imapLock = _imapFactory.GetLock(account.Id);
                            await imapLock.WaitAsync(ct);
                            try
                            {
                                if (!imap.IsConnected)
                                    await imap.ConnectAsync(account, ct);
                                await SyncFolderAsync(account, folder, imap, ct);
                            }
                            finally
                            {
                                imapLock.Release();
                            }
                        }, ct);
                    }
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        idleImap.Dispose();
    }
}
