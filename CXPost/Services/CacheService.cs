using CXPost.Data;
using CXPost.Models;

namespace CXPost.Services;

public class CacheService : ICacheService
{
    private readonly MailRepository _repo;

    public CacheService(MailRepository repo)
    {
        _repo = repo;
    }

    public void SyncFolders(string accountId, List<MailFolder> folders)
    {
        foreach (var folder in folders)
        {
            folder.AccountId = accountId;
            _repo.UpsertFolder(folder);
        }
    }

    public List<MailFolder> GetFolders(string accountId) => _repo.GetFolders(accountId);

    public void SyncHeaders(int folderId, List<MailMessage> messages)
    {
        foreach (var msg in messages)
        {
            msg.FolderId = folderId;
            _repo.UpsertMessage(msg);
        }
    }

    public List<MailMessage> GetMessages(int folderId) => _repo.GetMessages(folderId);

    public HashSet<uint> GetCachedUids(int folderId) => _repo.GetUids(folderId);

    public string? GetBody(int folderId, uint uid) => _repo.GetMessageBody(folderId, uid);

    public void StoreBody(int folderId, uint uid, string body) => _repo.StoreMessageBody(folderId, uid, body);

    public void UpdateFlags(int folderId, uint uid, bool isRead, bool isFlagged)
        => _repo.UpdateMessageFlags(folderId, uid, isRead, isFlagged);

    public void DeleteMessage(int folderId, uint uid) => _repo.DeleteMessage(folderId, uid);

    public void PurgeFolder(int folderId) => _repo.PurgeFolderMessages(folderId);
}
