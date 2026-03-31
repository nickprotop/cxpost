namespace CXPost.Models;

public class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public SecurityType ImapSecurity { get; set; } = SecurityType.Ssl;
    public SecurityType SmtpSecurity { get; set; } = SecurityType.StartTls;
    public DateTime? LastSync { get; set; }
    public string ReplyToAddress { get; set; } = string.Empty;
    public SignaturePosition SignaturePosition { get; set; } = SignaturePosition.BelowQuote;
    public string AutoBcc { get; set; } = string.Empty;
    public string DefaultCc { get; set; } = string.Empty;
    public string ReplyPrefix { get; set; } = "Re:";
    public string ForwardPrefix { get; set; } = "Fwd:";
    public int SyncIntervalSeconds { get; set; } = 300;
    public int MaxMessagesPerFolder { get; set; } = 0;
    public bool MarkAsReadOnView { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
}

public enum SecurityType
{
    None,
    Ssl,
    StartTls
}

public enum SignaturePosition
{
    BelowQuote,
    AboveQuote
}

public class CXPostConfig
{
    public List<Account> Accounts { get; set; } = [];
    public string Layout { get; set; } = "classic";
    public int SyncIntervalSeconds { get; set; } = 300;
    public bool Notifications { get; set; } = true;
}
