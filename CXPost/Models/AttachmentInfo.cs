namespace CXPost.Models;

public class AttachmentInfo
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string MimeType { get; set; } = "application/octet-stream";
    public int Index { get; set; }
}
