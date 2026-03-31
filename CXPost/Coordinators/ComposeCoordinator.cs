using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;
using MimeKit;

namespace CXPost.Coordinators;

public class ComposeCoordinator
{
    private readonly ISmtpService _smtp;
    private readonly IImapService _imap;
    private readonly ICacheService _cache;
    private readonly IContactsService _contacts;
    private readonly IConfigService _configService;
    private readonly NotificationCoordinator _notifications;

    public ComposeCoordinator(
        ISmtpService smtp,
        IImapService imap,
        ICacheService cache,
        IContactsService contacts,
        IConfigService configService,
        NotificationCoordinator notifications)
    {
        _smtp = smtp;
        _imap = imap;
        _cache = cache;
        _contacts = contacts;
        _configService = configService;
        _notifications = notifications;
    }

    public async Task SendAsync(Account account, string to, string? cc, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.Name, account.Email));

        foreach (var addr in ParseAddresses(to))
            message.To.Add(addr);

        if (!string.IsNullOrWhiteSpace(cc))
        {
            foreach (var addr in ParseAddresses(cc))
                message.Cc.Add(addr);
        }

        message.Subject = subject;

        // Add signature
        var bodyWithSig = body;
        if (!string.IsNullOrEmpty(account.Signature))
            bodyWithSig = body + "\n\n" + account.Signature;

        message.Body = new TextPart("plain") { Text = bodyWithSig };

        // Send via SMTP
        if (!_smtp.IsConnected)
            await _smtp.ConnectAsync(account, ct);
        await _smtp.SendAsync(message, ct);

        // Copy to Sent folder
        var folders = _cache.GetFolders(account.Id);
        var sent = folders.FirstOrDefault(f =>
            f.Path.Equals("Sent", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("[Gmail]/Sent", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("Sent Messages", StringComparison.OrdinalIgnoreCase));

        if (sent != null)
            await _imap.AppendMessageAsync(sent.Path, message, MailKit.MessageFlags.Seen, ct);

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

        var subject = MessageFormatter.GetReplySubject(original.Subject);
        var body = MessageFormatter.FormatQuotedReply(original);

        return (to, subject, body);
    }

    public (string to, string subject, string body) PrepareForward(MailMessage original)
    {
        var subject = MessageFormatter.GetForwardSubject(original.Subject);
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
