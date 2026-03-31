using CXPost.Models;

namespace CXPost.Services;

public interface ICacheService
{
    void SyncFolders(string accountId, List<MailFolder> folders);
    List<MailFolder> GetFolders(string accountId);
    void SyncHeaders(int folderId, List<MailMessage> messages);
    List<MailMessage> GetMessages(int folderId);
    HashSet<uint> GetCachedUids(int folderId);
    string? GetBody(int folderId, uint uid);
    void StoreBody(int folderId, uint uid, string body);
    void UpdateFlags(int folderId, uint uid, bool isRead, bool isFlagged);
    void DeleteMessage(int folderId, uint uid);
    void PurgeFolder(int folderId);
}
