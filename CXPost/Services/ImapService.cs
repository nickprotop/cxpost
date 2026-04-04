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

        // Cleanly close existing client before creating a new one
        if (_client != null)
        {
            try
            {
                if (_client.IsConnected)
                    await _client.DisconnectAsync(true, CancellationToken.None);
            }
            catch { }
            _client.Dispose();
            _client = null;
        }

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
                    TotalCount = f.Count,
                    FolderType = DetectFolderType(f)
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

    public async Task<(string? body, List<Models.AttachmentInfo> attachments)> FetchBodyAsync(
        string folderPath, uint uid, CancellationToken ct = default)
    {
        EnsureConnected();

        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            var message = await folder.GetMessageAsync(new UniqueId(uid), ct);

            var attachments = new List<Models.AttachmentInfo>();
            var attachIndex = 0;
            foreach (var attachment in message.Attachments)
            {
                var fileName = attachment is MimePart mp ? mp.FileName : null;
                long size = 0;
                if (attachment is MimePart mp2 && mp2.Content?.Stream != null)
                {
                    try { size = mp2.Content.Stream.Length; } catch { }
                }
                attachments.Add(new Models.AttachmentInfo
                {
                    FileName = fileName ?? $"attachment_{attachIndex}",
                    Size = size,
                    MimeType = attachment.ContentType?.MimeType ?? "application/octet-stream",
                    Index = attachIndex
                });
                attachIndex++;
            }

            return (message.HtmlBody ?? message.TextBody, attachments);
        }
        finally
        {
            // Don't close the folder — MailKit auto-closes when another folder is opened.
            // Explicit close on a persistent connection can trigger server BYE responses
            // that mark the connection as disconnected.
        }
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

    public async Task SaveAttachmentAsync(string folderPath, uint uid, int attachmentIndex,
        string targetPath, CancellationToken ct = default)
    {
        EnsureConnected();

        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            var message = await folder.GetMessageAsync(new UniqueId(uid), ct);

            var attachments = message.Attachments.ToList();
            if (attachmentIndex < 0 || attachmentIndex >= attachments.Count)
                throw new ArgumentOutOfRangeException(nameof(attachmentIndex));

            var attachment = attachments[attachmentIndex];
            if (attachment is MimePart part)
            {
                var finalPath = GetUniqueFilePath(targetPath);
                var dir = Path.GetDirectoryName(finalPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var stream = File.Create(finalPath);
                await part.Content.DecodeToAsync(stream, ct);
            }
        }
        finally
        {
            // Don't close — persistent connection, MailKit auto-closes on next folder open
        }
    }

    public async Task<List<(string TempPath, string FileName, long Size)>> FetchAttachmentsToTempAsync(
        string folderPath, uint uid, string tempDir, CancellationToken ct = default)
    {
        EnsureConnected();

        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
        var results = new List<(string TempPath, string FileName, long Size)>();

        var parts = message.BodyParts.OfType<MimePart>()
            .Where(p => p.Content != null && !string.IsNullOrEmpty(p.FileName))
            .ToList();

        if (!Directory.Exists(tempDir))
            Directory.CreateDirectory(tempDir);

        foreach (var part in parts)
        {
            var fileName = part.FileName ?? $"attachment-{results.Count}";
            var targetPath = GetUniqueFilePath(Path.Combine(tempDir, fileName));

            using var stream = File.Create(targetPath);
            await part.Content.DecodeToAsync(stream, ct);
            await stream.FlushAsync(ct);

            results.Add((targetPath, fileName, new FileInfo(targetPath).Length));
        }

        return results;
    }

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({counter}){ext}");
            counter++;
        } while (File.Exists(candidate));
        return candidate;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to IMAP server. Call ConnectAsync first.");
    }

    private static Models.FolderType DetectFolderType(MailKit.IMailFolder f)
    {
        var attrs = f.Attributes;

        // IMAP special-use attributes (RFC 6154)
        if (attrs.HasFlag(MailKit.FolderAttributes.Inbox)) return Models.FolderType.Inbox;
        if (attrs.HasFlag(MailKit.FolderAttributes.Sent))
            return Models.FolderType.Sent;
        if (attrs.HasFlag(MailKit.FolderAttributes.Drafts)) return Models.FolderType.Drafts;
        if (attrs.HasFlag(MailKit.FolderAttributes.Trash)) return Models.FolderType.Trash;
        if (attrs.HasFlag(MailKit.FolderAttributes.Junk)) return Models.FolderType.Spam;
        if (attrs.HasFlag(MailKit.FolderAttributes.Archive) || attrs.HasFlag(MailKit.FolderAttributes.All))
            return Models.FolderType.Archive;
        if (attrs.HasFlag(MailKit.FolderAttributes.Flagged)) return Models.FolderType.Starred;

        // Fallback: name heuristics
        return DetectFolderTypeByName(f.Name, f.FullName);
    }

    private static Models.FolderType DetectFolderTypeByName(string name, string path)
    {
        var lower = name.ToLowerInvariant();
        var pathLower = path.ToLowerInvariant();

        if (lower == "inbox" || pathLower == "inbox") return Models.FolderType.Inbox;
        if (lower.Contains("sent") || pathLower.Contains("sent")) return Models.FolderType.Sent;
        if (lower.Contains("draft") || pathLower.Contains("draft")) return Models.FolderType.Drafts;
        if (lower.Contains("trash") || lower.Contains("deleted") || pathLower.Contains("trash")) return Models.FolderType.Trash;
        if (lower.Contains("spam") || lower.Contains("junk") || pathLower.Contains("spam") || pathLower.Contains("junk")) return Models.FolderType.Spam;
        if (lower.Contains("archive") || pathLower.Contains("archive") || lower.Contains("all mail")) return Models.FolderType.Archive;
        if (lower.Contains("star") || lower.Contains("flagged") || pathLower.Contains("starred")) return Models.FolderType.Starred;
        if (lower.Contains("important") || pathLower.Contains("important")) return Models.FolderType.Important;

        return Models.FolderType.Other;
    }

    public void Dispose()
    {
        if (_client is { IsConnected: true })
        {
            try { _client.Disconnect(true); }
            catch { /* best effort */ }
        }
        _client?.Dispose();
    }
}
