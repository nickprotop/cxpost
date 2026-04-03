using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;
using MimeKit;

namespace CXPost.Coordinators;

public class ComposeCoordinator
{
    private readonly ISmtpService _smtp;
    private readonly ImapConnectionFactory _imapFactory;
    private readonly ICacheService _cache;
    private readonly IContactsService _contacts;
    private readonly IConfigService _configService;
    private readonly NotificationCoordinator _notifications;

    public ComposeCoordinator(
        ISmtpService smtp,
        ImapConnectionFactory imapFactory,
        ICacheService cache,
        IContactsService contacts,
        IConfigService configService,
        NotificationCoordinator notifications)
    {
        _smtp = smtp;
        _imapFactory = imapFactory;
        _cache = cache;
        _contacts = contacts;
        _configService = configService;
        _notifications = notifications;
    }

    public async Task SendAsync(Account account, string to, string? cc, string subject, string body, List<string>? attachmentPaths, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.Name, account.Email));

        if (!string.IsNullOrEmpty(account.ReplyToAddress))
            message.ReplyTo.Add(new MailboxAddress(account.Name, account.ReplyToAddress));

        foreach (var addr in ParseAddresses(to))
            message.To.Add(addr);

        if (!string.IsNullOrWhiteSpace(cc))
        {
            foreach (var addr in ParseAddresses(cc))
                message.Cc.Add(addr);
        }

        // Default Cc
        if (!string.IsNullOrEmpty(account.DefaultCc))
        {
            foreach (var addr in ParseAddresses(account.DefaultCc))
            {
                if (!message.Cc.Mailboxes.Any(m => m.Address.Equals(addr.Address, StringComparison.OrdinalIgnoreCase)))
                    message.Cc.Add(addr);
            }
        }

        // Auto-Bcc
        if (!string.IsNullOrEmpty(account.AutoBcc))
        {
            foreach (var addr in ParseAddresses(account.AutoBcc))
                message.Bcc.Add(addr);
        }

        message.Subject = subject;

        // Add signature
        var bodyWithSig = body;
        if (!string.IsNullOrEmpty(account.Signature))
            bodyWithSig = body + "\n\n" + account.Signature;

        if (attachmentPaths != null && attachmentPaths.Count > 0)
        {
            var multipart = new Multipart("mixed");
            multipart.Add(new TextPart("plain") { Text = bodyWithSig });

            foreach (var path in attachmentPaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Attachment not found: {path}");

                var mimeType = MimeTypes.GetMimeType(path);
                var attachment = new MimePart(mimeType)
                {
                    Content = new MimeContent(File.OpenRead(path), ContentEncoding.Default),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = Path.GetFileName(path)
                };
                multipart.Add(attachment);
            }
            message.Body = multipart;
        }
        else
        {
            message.Body = new TextPart("plain") { Text = bodyWithSig };
        }

        // Send via SMTP — always fresh connection to avoid stale state
        try
        {
            if (_smtp.IsConnected)
                await _smtp.DisconnectAsync(ct);
        }
        catch { /* ignore disconnect errors */ }

        await _smtp.ConnectAsync(account, ct);
        try
        {
            await _smtp.SendAsync(message, ct);
        }
        finally
        {
            try { await _smtp.DisconnectAsync(ct); }
            catch { /* best effort */ }

            // Dispose attachment streams
            if (message.Body is Multipart mp)
            {
                foreach (var part in mp.OfType<MimePart>())
                    part.Content?.Stream?.Dispose();
            }
        }

        // Copy to Sent folder
        var sent = FolderResolver.GetSent(account, _cache);

        if (sent != null)
        {
            using var imap = await _imapFactory.CreateConnectionAsync(account, ct);
            await imap.AppendMessageAsync(sent.Path, message, MailKit.MessageFlags.Seen, ct);
        }

        // Record contacts
        foreach (var addr in message.To.Mailboxes)
            _contacts.RecordContact(addr.Address, addr.Name);
        foreach (var addr in message.Cc.Mailboxes)
            _contacts.RecordContact(addr.Address, addr.Name);

        _notifications.NotifySendSuccess(to);
    }

    public (string to, string subject, string body) PrepareReply(Account account, MailMessage original, bool replyAll)
    {
        var to = original.FromAddress ?? "";
        if (replyAll && !string.IsNullOrEmpty(original.ToAddresses))
        {
            // Include all original recipients except self
            var allAddrs = System.Text.Json.JsonSerializer.Deserialize<List<string>>(original.ToAddresses) ?? [];
            var others = allAddrs.Where(a => !a.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
            var combined = new[] { to }.Concat(others).Distinct();
            to = string.Join(", ", combined);
        }

        var subject = MessageFormatter.GetReplySubject(original.Subject, account.ReplyPrefix);
        var quoted = MessageFormatter.FormatQuotedReply(original);

        string body;
        if (account.SignaturePosition == SignaturePosition.AboveQuote && !string.IsNullOrEmpty(account.Signature))
            body = "\n\n" + account.Signature + quoted;
        else
            body = quoted;

        return (to, subject, body);
    }

    public (string to, string subject, string body) PrepareForward(Account account, MailMessage original)
    {
        var subject = MessageFormatter.GetForwardSubject(original.Subject, account.ForwardPrefix);
        var body = MessageFormatter.FormatForwardBody(original);
        return ("", subject, body);
    }

    private static IEnumerable<MailboxAddress> ParseAddresses(string addresses)
    {
        foreach (var addr in addresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MailboxAddress.TryParse(addr, out var mailbox))
                yield return mailbox;
            else
                yield return new MailboxAddress(null, addr.Trim());
        }
    }
}
