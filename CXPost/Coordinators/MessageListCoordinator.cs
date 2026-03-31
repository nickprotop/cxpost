using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class MessageListCoordinator
{
    private readonly ICacheService _cache;
    private readonly IImapService _imap;
    private readonly MailSyncCoordinator _sync;
    private readonly Lazy<CXPostApp> _app;

    public MailFolder? CurrentFolder { get; private set; }
    public MailMessage? SelectedMessage { get; private set; }

    public MessageListCoordinator(
        ICacheService cache,
        IImapService imap,
        MailSyncCoordinator sync,
        Lazy<CXPostApp> app)
    {
        _cache = cache;
        _imap = imap;
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

    public async Task ToggleFlagAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;

        var newFlag = !SelectedMessage.IsFlagged;
        await _imap.SetFlagsAsync(CurrentFolder.Path, SelectedMessage.Uid, isFlagged: newFlag, ct: ct);
        _cache.UpdateFlags(CurrentFolder.Id, SelectedMessage.Uid, SelectedMessage.IsRead, newFlag);
        SelectedMessage.IsFlagged = newFlag;
        RefreshMessageList();
    }

    public async Task ToggleReadAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;

        var newRead = !SelectedMessage.IsRead;
        await _imap.SetFlagsAsync(CurrentFolder.Path, SelectedMessage.Uid, isRead: newRead, ct: ct);
        _cache.UpdateFlags(CurrentFolder.Id, SelectedMessage.Uid, newRead, SelectedMessage.IsFlagged);
        SelectedMessage.IsRead = newRead;
        RefreshMessageList();
    }

    public async Task DeleteMessageAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;

        // Move to Trash (or delete if already in Trash)
        var folders = _cache.GetFolders(CurrentFolder.AccountId);
        var trash = folders.FirstOrDefault(f =>
            f.Path.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("[Gmail]/Trash", StringComparison.OrdinalIgnoreCase));

        if (trash != null && CurrentFolder.Id != trash.Id)
        {
            await _imap.MoveMessageAsync(CurrentFolder.Path, trash.Path, SelectedMessage.Uid, ct);
        }
        else
        {
            await _imap.DeleteMessageAsync(CurrentFolder.Path, SelectedMessage.Uid, ct);
        }

        _cache.DeleteMessage(CurrentFolder.Id, SelectedMessage.Uid);
        SelectedMessage = null;
        RefreshMessageList();
    }

    public async Task FetchAndShowBodyAsync(MailMessage message, CancellationToken ct)
    {
        if (CurrentFolder == null) return;

        await _sync.FetchBodyAsync(CurrentFolder, message, ct);
        _app.Value.EnqueueUiAction(() => _app.Value.ShowMessagePreview(message));

        // Mark as read
        if (!message.IsRead)
        {
            await _imap.SetFlagsAsync(CurrentFolder.Path, message.Uid, isRead: true, ct: ct);
            _cache.UpdateFlags(CurrentFolder.Id, message.Uid, true, message.IsFlagged);
            message.IsRead = true;
            RefreshMessageList();
        }
    }
}
