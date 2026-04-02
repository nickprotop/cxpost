using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class MessageListCoordinator
{
    private readonly ICacheService _cache;
    private readonly ImapConnectionFactory _imapFactory;
    private readonly IConfigService _configService;
    private readonly MailSyncCoordinator _sync;
    private readonly Lazy<CXPostApp> _app;

    public MailFolder? CurrentFolder { get; private set; }
    public MailMessage? SelectedMessage { get; private set; }

    public MessageListCoordinator(
        ICacheService cache,
        ImapConnectionFactory imapFactory,
        IConfigService configService,
        MailSyncCoordinator sync,
        Lazy<CXPostApp> app)
    {
        _cache = cache;
        _imapFactory = imapFactory;
        _configService = configService;
        _sync = sync;
        _app = app;
    }

    public void SelectFolder(MailFolder folder)
    {
        CurrentFolder = folder;
        RefreshMessageList();
    }

    public void SelectMessage(MailMessage message)
    {
        SelectedMessage = message;
    }

    public void RefreshMessageList()
    {
        if (CurrentFolder == null) return;
        var messages = _cache.GetMessages(CurrentFolder.Id);
        _app.Value.EnqueueUiAction(() => _app.Value.PopulateMessageList(messages));
    }

    private Account? GetAccountForCurrentFolder()
    {
        // Prefer message's own account (for aggregated views)
        if (SelectedMessage?.AccountId != null)
        {
            var msgAccount = _configService.Load().Accounts.FirstOrDefault(a => a.Id == SelectedMessage.AccountId);
            if (msgAccount != null) return msgAccount;
        }
        return GetAccountForFolder(CurrentFolder);
    }

    private Account? GetAccountForFolder(MailFolder? folder)
    {
        if (folder == null) return null;
        return _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
    }

    public async Task ToggleFlagAsync(MailMessage message, MailFolder folder, CancellationToken ct)
    {
        var account = GetAccountForFolder(folder);
        if (account == null) return;

        var imap = _imapFactory.GetFetchConnection(account);
        var fetchLock = _imapFactory.GetFetchLock(account.Id);
        await fetchLock.WaitAsync(ct);
        var newFlag = !message.IsFlagged;
        try
        {
            await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
            await imap.SetFlagsAsync(folder.Path, message.Uid, isFlagged: newFlag, ct: CancellationToken.None);
        }
        finally { fetchLock.Release(); }
        _cache.UpdateFlags(folder.Id, message.Uid, message.IsRead, newFlag);
        message.IsFlagged = newFlag;
        RefreshMessageList();
    }

    public async Task ToggleReadAsync(MailMessage message, MailFolder folder, CancellationToken ct)
    {
        var account = GetAccountForFolder(folder);
        if (account == null) return;

        var imap = _imapFactory.GetFetchConnection(account);
        var fetchLock = _imapFactory.GetFetchLock(account.Id);
        await fetchLock.WaitAsync(ct);
        var newRead = !message.IsRead;
        try
        {
            await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
            await imap.SetFlagsAsync(folder.Path, message.Uid, isRead: newRead, ct: CancellationToken.None);
        }
        finally { fetchLock.Release(); }
        _cache.UpdateFlags(folder.Id, message.Uid, newRead, message.IsFlagged);
        message.IsRead = newRead;
        RefreshMessageList();
    }

    /// <summary>
    /// Optimistic delete: removes from cache/UI immediately, shows undo notification,
    /// then performs IMAP delete after undo window expires.
    /// </summary>
    public void DeleteMessageOptimistic(MailMessage message, MailFolder folder, CancellationToken ct)
    {
        var account = GetAccountForFolder(folder);
        if (account == null) return;

        // Optimistic: remove from cache and UI immediately
        _cache.DeleteMessage(folder.Id, message.Uid);
        if (SelectedMessage?.Uid == message.Uid)
            SelectedMessage = null;
        RefreshMessageList();

        // Undo window
        var undoCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var undoId = $"undo-delete-{message.Uid}";

        // Show undo notification via the app
        _app.Value.EnqueueUiAction(() =>
            _app.Value.ShowUndoNotification(undoId, "Message moved to Trash", () =>
            {
                // Undo: restore message to cache and refresh
                undoCts.Cancel();
                _cache.RestoreMessage(folder.Id, message);
                _app.Value.EnqueueUiAction(() =>
                {
                    RefreshMessageList();
                    _app.Value.ShowSuccess("Delete undone");
                });
            }));

        // Fire IMAP delete after undo window (5 seconds)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), undoCts.Token);

                // Undo window expired — perform server-side delete
                var imap = _imapFactory.GetFetchConnection(account);
                var fetchLock = _imapFactory.GetFetchLock(account.Id);
                await fetchLock.WaitAsync(ct);
                try
                {
                    await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                    var folders = _cache.GetFolders(folder.AccountId);
                    var trash = folders.FirstOrDefault(f =>
                        f.Path.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.Contains("[Gmail]/Trash", StringComparison.OrdinalIgnoreCase));

                    if (trash != null && folder.Id != trash.Id)
                        await imap.MoveMessageAsync(folder.Path, trash.Path, message.Uid, CancellationToken.None);
                    else
                        await imap.DeleteMessageAsync(folder.Path, message.Uid, CancellationToken.None);
                }
                finally { fetchLock.Release(); }

                _app.Value.EnqueueUiAction(() => _app.Value.DismissMessage(undoId));
                undoCts.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Check if this is an undo or a shutdown
                if (undoCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // Undo was triggered — IMAP delete cancelled, message already restored
                }
                else
                {
                    // App shutdown — restore message since server delete didn't happen
                    _cache.RestoreMessage(folder.Id, message);
                    _app.Value.EnqueueUiAction(() =>
                    {
                        RefreshMessageList();
                        _app.Value.DismissMessage(undoId);
                    });
                }
                undoCts.Dispose();
            }
            catch (Exception ex)
            {
                // IMAP failed — restore message locally
                _cache.RestoreMessage(folder.Id, message);
                _app.Value.EnqueueUiAction(() =>
                {
                    RefreshMessageList();
                    _app.Value.DismissMessage(undoId);
                    _app.Value.ShowError($"Delete failed: {ex.Message}");
                });
                undoCts.Dispose();
            }
        }, ct);
    }

    public async Task FetchAndShowBodyAsync(MailMessage message, CancellationToken ct)
    {
        if (CurrentFolder == null) return;

        // Fetch body with a timeout so we don't hang forever
        using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fetchTimeout.CancelAfter(TimeSpan.FromSeconds(30));

        await _sync.FetchBodyAsync(CurrentFolder, message, fetchTimeout.Token);

        // Mark as read — fire and forget, don't block the UI for a flag update
        if (!message.IsRead)
        {
            var account = GetAccountForCurrentFolder();
            var folder = CurrentFolder;
            if (account != null && account.MarkAsReadOnView)
            {
                // Update locally immediately
                _cache.UpdateFlags(folder.Id, message.Uid, true, message.IsFlagged);
                message.IsRead = true;
                RefreshMessageList();

                // Sync to server in background (non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var fetchLock = _imapFactory.GetFetchLock(account.Id);
                        await fetchLock.WaitAsync(ct);
                        try
                        {
                            var imap = _imapFactory.GetFetchConnection(account);
                            await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                            await imap.SetFlagsAsync(folder.Path, message.Uid, isRead: true, ct: CancellationToken.None);
                        }
                        finally { fetchLock.Release(); }
                    }
                    catch { /* best effort — local flag is already set */ }
                }, ct);
            }
        }
    }
}
