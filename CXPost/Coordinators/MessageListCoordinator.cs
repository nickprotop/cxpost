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

    private Account? GetAccountForCurrentFolder()
    {
        if (CurrentFolder == null) return null;
        return _configService.Load().Accounts.FirstOrDefault(a => a.Id == CurrentFolder.AccountId);
    }

    public async Task ToggleFlagAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;
        var account = GetAccountForCurrentFolder();
        if (account == null) return;

        using var imap = await CreateEphemeralImapAsync(account, ct);
        var newFlag = !SelectedMessage.IsFlagged;
        await imap.SetFlagsAsync(CurrentFolder.Path, SelectedMessage.Uid, isFlagged: newFlag, ct: ct);
        _cache.UpdateFlags(CurrentFolder.Id, SelectedMessage.Uid, SelectedMessage.IsRead, newFlag);
        SelectedMessage.IsFlagged = newFlag;
        RefreshMessageList();
    }

    public async Task ToggleReadAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;
        var account = GetAccountForCurrentFolder();
        if (account == null) return;

        using var imap = await CreateEphemeralImapAsync(account, ct);
        var newRead = !SelectedMessage.IsRead;
        await imap.SetFlagsAsync(CurrentFolder.Path, SelectedMessage.Uid, isRead: newRead, ct: ct);
        _cache.UpdateFlags(CurrentFolder.Id, SelectedMessage.Uid, newRead, SelectedMessage.IsFlagged);
        SelectedMessage.IsRead = newRead;
        RefreshMessageList();
    }

    public async Task DeleteMessageAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;
        var account = GetAccountForCurrentFolder();
        if (account == null) return;

        using var imap = await CreateEphemeralImapAsync(account, ct);
        var folders = _cache.GetFolders(CurrentFolder.AccountId);
        var trash = folders.FirstOrDefault(f =>
            f.Path.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("[Gmail]/Trash", StringComparison.OrdinalIgnoreCase));

        if (trash != null && CurrentFolder.Id != trash.Id)
            await imap.MoveMessageAsync(CurrentFolder.Path, trash.Path, SelectedMessage.Uid, ct);
        else
            await imap.DeleteMessageAsync(CurrentFolder.Path, SelectedMessage.Uid, ct);

        _cache.DeleteMessage(CurrentFolder.Id, SelectedMessage.Uid);
        SelectedMessage = null;
        RefreshMessageList();
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
            if (account == null || account.MarkAsReadOnView)
            {
                using var imap = await CreateEphemeralImapAsync(account!, ct);
                await imap.SetFlagsAsync(CurrentFolder.Path, message.Uid, isRead: true, ct: ct);
                _cache.UpdateFlags(CurrentFolder.Id, message.Uid, true, message.IsFlagged);
                message.IsRead = true;
                RefreshMessageList();
            }
        }
    }
}
