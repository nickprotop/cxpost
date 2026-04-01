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

    /// <summary>
    /// Creates a short-lived IMAP connection for user-initiated actions (flag, delete, move).
    /// Uses its own client to avoid blocking on the sync connection's lock.
    /// </summary>
    private async Task<ImapService> CreateEphemeralImapAsync(Account account, CancellationToken ct)
    {
        var imap = new ImapService(_imapFactory.Credentials);
        await imap.ConnectAsync(account, ct);
        return imap;
    }

    private Account? GetAccountForCurrentFolder() => GetAccountForFolder(CurrentFolder);

    private Account? GetAccountForFolder(MailFolder? folder)
    {
        if (folder == null) return null;
        return _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
    }

    public async Task ToggleFlagAsync(MailMessage message, MailFolder folder, CancellationToken ct)
    {
        var account = GetAccountForFolder(folder);
        if (account == null) return;

        using var imap = await CreateEphemeralImapAsync(account, ct);
        var newFlag = !message.IsFlagged;
        await imap.SetFlagsAsync(folder.Path, message.Uid, isFlagged: newFlag, ct: ct);
        _cache.UpdateFlags(folder.Id, message.Uid, message.IsRead, newFlag);
        message.IsFlagged = newFlag;
        RefreshMessageList();
    }

    public async Task ToggleReadAsync(MailMessage message, MailFolder folder, CancellationToken ct)
    {
        var account = GetAccountForFolder(folder);
        if (account == null) return;

        using var imap = await CreateEphemeralImapAsync(account, ct);
        var newRead = !message.IsRead;
        await imap.SetFlagsAsync(folder.Path, message.Uid, isRead: newRead, ct: ct);
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
                using var imap = await CreateEphemeralImapAsync(account, ct);
                var folders = _cache.GetFolders(folder.AccountId);
                var trash = folders.FirstOrDefault(f =>
                    f.Path.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
                    f.Path.Contains("[Gmail]/Trash", StringComparison.OrdinalIgnoreCase));

                if (trash != null && folder.Id != trash.Id)
                    await imap.MoveMessageAsync(folder.Path, trash.Path, message.Uid, ct);
                else
                    await imap.DeleteMessageAsync(folder.Path, message.Uid, ct);

                _app.Value.EnqueueUiAction(() => _app.Value.DismissMessage(undoId));
            }
            catch (OperationCanceledException)
            {
                // Undo was triggered — IMAP delete cancelled
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
            }
        }, ct);
    }

    public async Task FetchAndShowBodyAsync(MailMessage message, CancellationToken ct)
    {
        if (CurrentFolder == null) return;

        await _sync.FetchBodyAsync(CurrentFolder, message, ct);
        _app.Value.EnqueueUiAction(() => _app.Value.ShowMessagePreview(message));

        // Mark as read (if account setting allows)
        if (!message.IsRead)
        {
            var account = GetAccountForCurrentFolder();
            if (account != null && account.MarkAsReadOnView)
            {
                using var imap = await CreateEphemeralImapAsync(account, ct);
                await imap.SetFlagsAsync(CurrentFolder.Path, message.Uid, isRead: true, ct: ct);
                _cache.UpdateFlags(CurrentFolder.Id, message.Uid, true, message.IsFlagged);
                message.IsRead = true;
                RefreshMessageList();
            }
        }
    }
}
