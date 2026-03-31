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
}

public enum SecurityType
{
    None,
    Ssl,
    StartTls
}

public class CXPostConfig
{
    public List<Account> Accounts { get; set; } = [];
    public string Layout { get; set; } = "classic";
    public int SyncIntervalSeconds { get; set; } = 300;
    public bool Notifications { get; set; } = true;
}
