namespace CXPost.Models;

public class MailFolder
{
    public int Id { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public uint UidValidity { get; set; }
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
}
