namespace CXPost.Models;

public class MailThread
{
    public string ThreadId { get; set; } = string.Empty;
    public List<MailMessage> Messages { get; set; } = [];
    public string? Subject => Messages.FirstOrDefault()?.Subject;
    public DateTime LatestDate => Messages.Max(m => m.Date);
    public int UnreadCount => Messages.Count(m => !m.IsRead);
    public int MessageCount => Messages.Count;
}
