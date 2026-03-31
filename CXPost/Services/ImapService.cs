using CXPost.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace CXPost.Services;

public class ImapService : IImapService, IDisposable
{
    private readonly ICredentialService _credentials;
    private ImapClient? _client;
    private Account? _account;

    public ImapService(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public bool IsConnected => _client?.IsConnected == true && _client.IsAuthenticated;

    public async Task ConnectAsync(Account account, CancellationToken ct = default)
    {
        _account = account;
        _client = new ImapClient();

        var socketOptions = account.ImapSecurity switch
        {
            SecurityType.Ssl => SecureSocketOptions.SslOnConnect,
            SecurityType.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };
        await _client.ConnectAsync(account.ImapHost, account.ImapPort, socketOptions, ct);

        var password = _credentials.GetPassword(account.Id) ?? string.Empty;
        await _client.AuthenticateAsync(account.Username.Length > 0 ? account.Username : account.Email, password, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client is { IsConnected: true })
            await _client.DisconnectAsync(true, ct);
    }

    public async Task<List<Models.MailFolder>> GetFoldersAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var ns = _client!.PersonalNamespaces[0];
        var imapFolders = await _client.GetFoldersAsync(ns, cancellationToken: ct);

        var result = new List<Models.MailFolder>();
        foreach (var f in imapFolders)
        {
            if (!f.Exists) continue;
            try
            {
                await f.OpenAsync(FolderAccess.ReadOnly, ct);
                result.Add(new Models.MailFolder
                {
                    AccountId = _account!.Id,
                    Path = f.FullName,
                    DisplayName = f.Name,
                    UidValidity = f.UidValidity,
                    UnreadCount = f.Unread,
                    TotalCount = f.Count
                });
                await f.CloseAsync(false, ct);
            }
            catch (Exception)
            {
                // Skip folders we can't open (e.g. \Noselect)
            }
        }
        return result;
    }

    public async Task<List<Models.MailMessage>> FetchHeadersAsync(string folderPath, uint? sinceUid = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        IList<IMessageSummary> summaries;
        if (sinceUid.HasValue)
        {
            var range = new UniqueIdRange(new UniqueId(sinceUid.Value), UniqueId.MaxValue);
            summaries = await folder.FetchAsync(range,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                MessageSummaryItems.BodyStructure | MessageSummaryItems.References, ct);
        }
        else
        {
            summaries = await folder.FetchAsync(0, -1,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                MessageSummaryItems.BodyStructure | MessageSummaryItems.References, ct);
        }

        var messages = new List<Models.MailMessage>();
        foreach (var s in summaries)
        {
            var msg = new Models.MailMessage
            {
                Uid = s.UniqueId.Id,
                MessageId = s.Envelope.MessageId,
                InReplyTo = s.Envelope.InReplyTo,
                References = s.References != null ? string.Join(" ", s.References.Select(r => r.ToString())) : null,
                FromName = s.Envelope.From.Mailboxes.FirstOrDefault()?.Name,
                FromAddress = s.Envelope.From.Mailboxes.FirstOrDefault()?.Address,
                ToAddresses = System.Text.Json.JsonSerializer.Serialize(
                    s.Envelope.To.Mailboxes.Select(m => m.Address).ToList()),
                CcAddresses = s.Envelope.Cc.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(s.Envelope.Cc.Mailboxes.Select(m => m.Address).ToList())
                    : null,
                Subject = s.Envelope.Subject,
                Date = s.Envelope.Date?.UtcDateTime ?? DateTime.UtcNow,
                IsRead = s.Flags?.HasFlag(MessageFlags.Seen) == true,
                IsFlagged = s.Flags?.HasFlag(MessageFlags.Flagged) == true,
                HasAttachments = s.Body is MailKit.BodyPartMultipart multi && multi.BodyParts.Any(p => p is MailKit.BodyPartBasic b && b.IsAttachment)
            };
            messages.Add(msg);
        }

        await folder.CloseAsync(false, ct);
        return messages;
    }

    public async Task<string?> FetchBodyAsync(string folderPath, uint uid, CancellationToken ct = default)
    {
        // Use a separate connection for body fetch to avoid conflicting
        // with the main connection (which may be in IDLE or syncing)
        if (_account == null)
            throw new InvalidOperationException("No account configured. Call ConnectAsync first.");

        using var client = new ImapClient();
        var socketOptions = _account.ImapSecurity switch
        {
            SecurityType.Ssl => SecureSocketOptions.SslOnConnect,
            SecurityType.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };
        await client.ConnectAsync(_account.ImapHost, _account.ImapPort, socketOptions, ct);

        var password = _credentials.GetPassword(_account.Id) ?? string.Empty;
        await client.AuthenticateAsync(
            _account.Username.Length > 0 ? _account.Username : _account.Email,
            password, ct);

        var folder = await client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
        await folder.CloseAsync(false, ct);
        await client.DisconnectAsync(true, ct);

        return message.TextBody ?? message.HtmlBody;
    }

    public async Task SetFlagsAsync(string folderPath, uint uid, bool? isRead = null, bool? isFlagged = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var uniqueId = new UniqueId(uid);
        if (isRead.HasValue)
        {
            if (isRead.Value)
                await folder.AddFlagsAsync(uniqueId, MessageFlags.Seen, true, ct);
            else
                await folder.RemoveFlagsAsync(uniqueId, MessageFlags.Seen, true, ct);
        }
        if (isFlagged.HasValue)
        {
            if (isFlagged.Value)
                await folder.AddFlagsAsync(uniqueId, MessageFlags.Flagged, true, ct);
            else
                await folder.RemoveFlagsAsync(uniqueId, MessageFlags.Flagged, true, ct);
        }

        await folder.CloseAsync(false, ct);
    }

    public async Task MoveMessageAsync(string sourcePath, string destPath, uint uid, CancellationToken ct = default)
    {
        EnsureConnected();
        var source = await _client!.GetFolderAsync(sourcePath, ct);
        var dest = await _client.GetFolderAsync(destPath, ct);
        await source.OpenAsync(FolderAccess.ReadWrite, ct);
        await source.MoveToAsync(new UniqueId(uid), dest, ct);
        await source.CloseAsync(false, ct);
    }

    public async Task DeleteMessageAsync(string folderPath, uint uid, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, ct);
        await folder.ExpungeAsync(ct);
        await folder.CloseAsync(false, ct);
    }

    public async Task<uint> AppendMessageAsync(string folderPath, MimeMessage message, MessageFlags flags = MessageFlags.Seen, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        var uid = await folder.AppendAsync(message, flags, ct);
        await folder.CloseAsync(false, ct);
        return uid?.Id ?? 0;
    }

    public async Task CreateFolderAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var ns = _client!.PersonalNamespaces[0];
        var topLevel = _client.GetFolder(ns);
        await topLevel.CreateAsync(folderPath, true, ct);
    }

    public async Task RenameFolderAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(oldPath, ct);
        var ns = _client.PersonalNamespaces[0];
        var parent = _client.GetFolder(ns);
        await folder.RenameAsync(parent, newPath, ct);
    }

    public async Task DeleteFolderAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.DeleteAsync(ct);
    }

    public async Task<List<uint>> SearchAsync(string folderPath, string query, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var searchQuery = SearchQuery.SubjectContains(query)
            .Or(SearchQuery.FromContains(query))
            .Or(SearchQuery.BodyContains(query));

        var results = await folder.SearchAsync(searchQuery, ct);
        await folder.CloseAsync(false, ct);
        return results.Select(uid => uid.Id).ToList();
    }

    public async Task IdleAsync(string folderPath, Action onNewMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        folder.CountChanged += (_, _) => onNewMessage();

        using var done = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, done.Token);

        // Re-IDLE every 29 minutes (IMAP spec)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(29), ct);
                done.Cancel();
            }
            catch (OperationCanceledException) { }
        }, ct);

        try
        {
            await _client!.IdleAsync(linked.Token);
        }
        catch (OperationCanceledException) { }

        await folder.CloseAsync(false, CancellationToken.None);
    }

    public async Task<uint> GetUidValidityAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var validity = folder.UidValidity;
        await folder.CloseAsync(false, ct);
        return validity;
    }

    public async Task<HashSet<uint>> GetUidsAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var all = await folder.SearchAsync(SearchQuery.All, ct);
        await folder.CloseAsync(false, ct);
        return all.Select(u => u.Id).ToHashSet();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to IMAP server. Call ConnectAsync first.");
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
