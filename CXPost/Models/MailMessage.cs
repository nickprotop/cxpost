namespace CXPost.Models;

public class MailMessage
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public uint Uid { get; set; }
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
    public string? References { get; set; }
    public string? ThreadId { get; set; }
    public string? FromName { get; set; }
    public string? FromAddress { get; set; }
    public string? ToAddresses { get; set; }
    public string? CcAddresses { get; set; }
    public string? Subject { get; set; }
    public DateTime Date { get; set; }
    public bool IsRead { get; set; }
    public bool IsFlagged { get; set; }
    public bool HasAttachments { get; set; }
    public string? BodyPlain { get; set; }
    public bool BodyFetched { get; set; }
    public List<AttachmentInfo>? Attachments { get; set; }
}
