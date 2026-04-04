using CXPost.Models;

namespace CXPost.Services;

public interface IImapService
{
    Task ConnectAsync(Account account, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    Task<List<MailFolder>> GetFoldersAsync(CancellationToken ct = default);
    Task<List<MailMessage>> FetchHeadersAsync(string folderPath, uint? sinceUid = null, CancellationToken ct = default);
    Task<(string? body, List<Models.AttachmentInfo> attachments)> FetchBodyAsync(string folderPath, uint uid, CancellationToken ct = default);
    Task SetFlagsAsync(string folderPath, uint uid, bool? isRead = null, bool? isFlagged = null, CancellationToken ct = default);
    Task MoveMessageAsync(string sourcePath, string destPath, uint uid, CancellationToken ct = default);
    Task DeleteMessageAsync(string folderPath, uint uid, CancellationToken ct = default);
    Task<uint> AppendMessageAsync(string folderPath, MimeKit.MimeMessage message, MailKit.MessageFlags flags = MailKit.MessageFlags.Seen, CancellationToken ct = default);
    Task CreateFolderAsync(string folderPath, CancellationToken ct = default);
    Task RenameFolderAsync(string oldPath, string newPath, CancellationToken ct = default);
    Task DeleteFolderAsync(string folderPath, CancellationToken ct = default);
    Task<List<uint>> SearchAsync(string folderPath, string query, CancellationToken ct = default);
    Task IdleAsync(string folderPath, Action onNewMessage, CancellationToken ct = default);
    Task<uint> GetUidValidityAsync(string folderPath, CancellationToken ct = default);
    Task<HashSet<uint>> GetUidsAsync(string folderPath, CancellationToken ct = default);
    Task SaveAttachmentAsync(string folderPath, uint uid, int attachmentIndex, string targetPath, CancellationToken ct = default);
    Task<List<(string TempPath, string FileName, long Size)>> FetchAttachmentsToTempAsync(
        string folderPath, uint uid, string tempDir, CancellationToken ct = default);
}
