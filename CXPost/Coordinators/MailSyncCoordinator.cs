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
    private readonly ConcurrentDictionary<int, bool> _syncingFolderIds = new();
    private readonly ConcurrentDictionary<string, bool> _fetchingBodies = new();

    /// <summary>
    /// Set of folder IDs currently being synced — used by UI for sync animation.
    /// </summary>
    public ICollection<int> SyncingFolderIds => _syncingFolderIds.Keys;

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
        {
            ImapLogger.Debug($"[{account.Name}] SyncAccount skipped — already syncing");
            return;
        }
        ImapLogger.Info($"[{account.Name}] SyncAccount started");

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
                // Connect with retry — sync connection may have been dropped by server
                if (!imap.IsConnected)
                {
                    ImapLogger.Debug($"[{account.Name}] Sync connection not connected, connecting...");
                    try
                    {
                        await imap.ConnectAsync(account, ct);
                        ImapLogger.Debug($"[{account.Name}] Sync connection established");
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        ImapLogger.Warn($"[{account.Name}] Sync connect failed: {ex.Message} — retrying in 2s");
                        await Task.Delay(2000, ct);
                        try { await imap.DisconnectAsync(CancellationToken.None); } catch { }
                        await imap.ConnectAsync(account, ct);
                        ImapLogger.Debug($"[{account.Name}] Sync retry connected");
                    }
                }

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

                    _syncingFolderIds.TryAdd(folder.Id, true);
                    _app.Value.EnqueueUiAction(() =>
                    {
                        _app.Value.ReplaceMessage(syncMsgId,
                            $"{account.Name}: Syncing {folder.DisplayName} ({progress}/{total})...");
                        _app.Value.RefreshFolderTree();
                    });

                    var beforeCount = _cache.GetCachedUids(folder.Id).Count;
                    await SyncFolderAsync(account, folder, imap, ct);
                    var afterCount = _cache.GetCachedUids(folder.Id).Count;
                    totalMessages += afterCount - beforeCount;

                    _syncingFolderIds.TryRemove(folder.Id, out _);
                    _app.Value.EnqueueUiAction(() =>
                    {
                        _app.Value.RefreshFolderTree();
                        _app.Value.RefreshCurrentMessageListIfFolder(folder.Id);
                    });
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

            ImapLogger.Info($"[{account.Name}] SyncAccount completed: {totalMessages} new messages");
            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.DismissMessage(syncMsgId);
                var globalNotify = _configService.Load().Notifications;
                if (globalNotify && account.NotificationsEnabled)
                    _notifications.NotifySyncComplete(account.Name, totalMessages);
            });
        }
        catch (OperationCanceledException)
        {
            ImapLogger.Debug($"[{account.Name}] SyncAccount cancelled");
            _app.Value.EnqueueUiAction(() => _app.Value.DismissMessage(syncMsgId));
        }
        catch (Exception ex)
        {
            ImapLogger.Error($"[{account.Name}] SyncAccount failed: {ex.Message}", ex);
            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.DismissMessage(syncMsgId);
                _app.Value.ShowWarning($"{account.Name}: Sync retry — {ex.Message}");
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

            foreach (var h in newHeaders)
                h.AccountId = account.Id;

            var allMessages = _cache.GetMessages(folder.Id);
            allMessages.AddRange(newHeaders);
            _threading.AssignThreadIds(allMessages);

            _cache.SyncHeaders(folder.Id, newHeaders);
        }
    }

    /// <summary>
    /// Syncs a single folder by opening a connection, syncing, and releasing.
    /// Used by the per-folder sync (Shift+F5) action.
    /// </summary>
    public async Task SyncSingleFolderAsync(Account account, MailFolder folder, CancellationToken ct)
    {
        var imap = _imapFactory.GetConnection(account);
        var imapLock = _imapFactory.GetLock(account.Id);

        var syncMsgId = $"sync-folder-{folder.Id}";
        _syncingFolderIds.TryAdd(folder.Id, true);

        try
        {
            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.ReplaceMessage(syncMsgId,
                    $"Syncing {folder.DisplayName}...");
                _app.Value.RefreshFolderTree();
            });

            await imapLock.WaitAsync(ct);
            try
            {
                if (!imap.IsConnected)
                {
                    try
                    {
                        await imap.ConnectAsync(account, ct);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        ImapLogger.Warn($"[{account.Name}] Folder sync connect failed: {ex.Message} — retrying");
                        await Task.Delay(2000, ct);
                        try { await imap.DisconnectAsync(CancellationToken.None); } catch { }
                        await imap.ConnectAsync(account, ct);
                    }
                }

                await SyncFolderAsync(account, folder, imap, ct);
            }
            finally
            {
                imapLock.Release();
            }

            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.DismissMessage(syncMsgId);
                _app.Value.RefreshFolderTree();
                _app.Value.RefreshCurrentMessageListIfFolder(folder.Id);
                _app.Value.ShowInfo($"Synced {folder.DisplayName}");
            });
        }
        catch (OperationCanceledException)
        {
            _app.Value.EnqueueUiAction(() => _app.Value.DismissMessage(syncMsgId));
        }
        catch (Exception ex)
        {
            ImapLogger.Error($"[{account.Name}] Folder sync failed for {folder.DisplayName}: {ex.Message}", ex);
            _app.Value.EnqueueUiAction(() =>
            {
                _app.Value.DismissMessage(syncMsgId);
                _app.Value.ShowWarning($"Sync failed for {folder.DisplayName}: {ex.Message}");
            });
        }
        finally
        {
            _syncingFolderIds.TryRemove(folder.Id, out _);
            _app.Value.EnqueueUiAction(() => _app.Value.RefreshFolderTree());
        }
    }

    public async Task FetchBodyAsync(MailFolder folder, MailMessage message, CancellationToken ct)
    {
        // If body is cached but attachments aren't populated yet, still need to fetch
        if (message.BodyFetched && (message.Attachments != null || !message.HasAttachments))
            return;

        var key = $"{folder.Id}:{message.Uid}";
        if (!_fetchingBodies.TryAdd(key, true))
        {
            ImapLogger.Debug($"FetchBody skipped — already fetching {key}");
            return;
        }

        ImapLogger.Debug($"FetchBody started: folder={folder.DisplayName} uid={message.Uid} subject={message.Subject}");

        try
        {
            var account = _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
            if (account == null) return;

            // Ephemeral connection — no lock needed, properly cancellable
            using var imap = await _imapFactory.CreateConnectionAsync(account, ct);
            ImapLogger.Debug($"FetchBody({key}) fetching from server...");
            var (body, attachments) = await imap.FetchBodyAsync(folder.Path, message.Uid, ct);
            ImapLogger.Debug($"FetchBody({key}) received body={body?.Length ?? 0} chars, {attachments.Count} attachments");

            if (!message.BodyFetched && body != null)
            {
                var attachmentList = attachments.Count > 0 ? attachments : null;
                _cache.StoreBody(folder.Id, message.Uid, body, attachmentList);
                message.BodyPlain = body;
                message.BodyFetched = true;
                message.Attachments = attachmentList;
            }
            else if (message.Attachments == null && attachments.Count > 0)
            {
                message.Attachments = attachments;
            }
        }
        finally
        {
            _fetchingBodies.TryRemove(key, out _);
        }
    }

    public async Task StartIdleAsync(Account account, string folderPath, CancellationToken ct)
    {
        // IDLE uses a dedicated factory-managed connection
        var idleImap = _imapFactory.GetIdleConnection(account);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _imapFactory.EnsureConnectedAsync(idleImap, account, ct);

                var debounceTimer = new System.Threading.Timer(_ =>
                {
                    var folder = _cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == folderPath);
                    if (folder != null && !_syncingFolderIds.ContainsKey(folder.Id))
                    {
                        _ = Task.Run(async () =>
                        {
                            var syncImap = _imapFactory.GetSyncConnection(account);
                            var syncLock = _imapFactory.GetSyncLock(account.Id);
                            if (!syncLock.Wait(0)) return; // skip if sync already running
                            try
                            {
                                await _imapFactory.EnsureConnectedAsync(syncImap, account, ct);
                                await SyncFolderAsync(account, folder, syncImap, ct);
                                _app.Value.EnqueueUiAction(() =>
                                {
                                    _app.Value.RefreshFolderTree();
                                    _app.Value.RefreshCurrentMessageListIfFolder(folder.Id);
                                });
                            }
                            catch (OperationCanceledException) { }
                            catch { /* IDLE sync failure — will retry on next event */ }
                            finally { syncLock.Release(); }
                        }, ct);
                    }
                }, null, Timeout.Infinite, Timeout.Infinite);

                await idleImap.IdleAsync(folderPath, () =>
                {
                    // Debounce: reset timer to 2 seconds — coalesces burst of events
                    debounceTimer.Change(2000, Timeout.Infinite);
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
