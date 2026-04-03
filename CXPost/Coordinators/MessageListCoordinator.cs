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
        // Don't call RefreshMessageList here — the caller (OnFolderSelected)
        // directly calls PopulateMessageList after this, and the queued
        // RefreshMessageList would race with that and cause stale data.
    }

    public void SelectMessage(MailMessage message)
    {
        SelectedMessage = message;
    }

    public void RefreshMessageList()
    {
        if (CurrentFolder == null) return;
        var folderId = CurrentFolder.Id;
        var messages = _cache.GetMessages(folderId);
        _app.Value.EnqueueUiAction(() =>
        {
            // Only apply if this folder is still selected (prevents stale queued updates)
            if (CurrentFolder?.Id == folderId)
                _app.Value.PopulateMessageList(messages);
        });
    }

    private Account? GetAccountForCurrentFolder()
    {
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

        var newFlag = !message.IsFlagged;

        // Update locally first (instant UI)
        _cache.UpdateFlags(folder.Id, message.Uid, message.IsRead, newFlag);
        message.IsFlagged = newFlag;
        RefreshMessageList();

        // Sync to server with ephemeral connection
        using var imap = await _imapFactory.CreateConnectionAsync(account, ct);
        await imap.SetFlagsAsync(folder.Path, message.Uid, isFlagged: newFlag, ct: ct);
    }

    public async Task ToggleReadAsync(MailMessage message, MailFolder folder, CancellationToken ct)
    {
        var account = GetAccountForFolder(folder);
        if (account == null) return;

        var newRead = !message.IsRead;

        // Update locally first (instant UI)
        _cache.UpdateFlags(folder.Id, message.Uid, newRead, message.IsFlagged);
        message.IsRead = newRead;
        RefreshMessageList();

        // Sync to server with ephemeral connection
        using var imap = await _imapFactory.CreateConnectionAsync(account, ct);
        await imap.SetFlagsAsync(folder.Path, message.Uid, isRead: newRead, ct: ct);
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

        _app.Value.EnqueueUiAction(() =>
            _app.Value.ShowUndoNotification(undoId, "Message moved to Trash", () =>
            {
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

                // Undo window expired — ephemeral connection for server delete
                using var imap = await _imapFactory.CreateConnectionAsync(account, ct);
                var trash = FolderResolver.GetTrash(account, _cache);

                if (trash != null && folder.Id != trash.Id)
                    await imap.MoveMessageAsync(folder.Path, trash.Path, message.Uid, ct);
                else
                    await imap.DeleteMessageAsync(folder.Path, message.Uid, ct);

                _app.Value.EnqueueUiAction(() => _app.Value.DismissMessage(undoId));
                undoCts.Dispose();
            }
            catch (OperationCanceledException)
            {
                if (undoCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // Undo triggered — message already restored
                }
                else
                {
                    // App shutdown — restore since server delete didn't happen
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

        using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fetchTimeout.CancelAfter(TimeSpan.FromSeconds(30));

        await _sync.FetchBodyAsync(CurrentFolder, message, fetchTimeout.Token);

        // Mark as read — update locally first, then sync to server in background
        if (!message.IsRead)
        {
            var account = GetAccountForCurrentFolder();
            var folder = CurrentFolder;
            if (account != null && account.MarkAsReadOnView)
            {
                _cache.UpdateFlags(folder.Id, message.Uid, true, message.IsFlagged);
                message.IsRead = true;
                RefreshMessageList();

                // Background server sync with ephemeral connection
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var imap = await _imapFactory.CreateConnectionAsync(account, ct);
                        await imap.SetFlagsAsync(folder.Path, message.Uid, isRead: true, ct: ct);
                    }
                    catch { /* best effort — local flag already set */ }
                }, ct);
            }
        }
    }
}
