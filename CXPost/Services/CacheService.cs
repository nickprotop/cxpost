using CXPost.Data;
using CXPost.Models;

namespace CXPost.Services;

public class CacheService : ICacheService
{
    private readonly MailRepository _repo;

    // In-memory caches — invalidated on writes
    private readonly Dictionary<int, List<MailMessage>> _headersCache = new();
    private readonly Dictionary<int, HashSet<uint>> _uidsCache = new();
    private readonly object _cacheLock = new();

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
        using var tx = _repo.BeginTransaction();
        foreach (var msg in messages)
        {
            msg.FolderId = folderId;
            _repo.UpsertMessage(msg);
        }
        tx.Commit();
        InvalidateFolder(folderId);
    }

    public List<MailMessage> GetMessages(int folderId) => _repo.GetMessages(folderId);

    public List<MailMessage> GetMessageHeaders(int folderId)
    {
        lock (_cacheLock)
        {
            if (_headersCache.TryGetValue(folderId, out var cached))
                return cached;
        }
        var headers = _repo.GetMessages(folderId);
        lock (_cacheLock)
        {
            _headersCache[folderId] = headers;
        }
        return headers;
    }

    public HashSet<uint> GetCachedUids(int folderId)
    {
        lock (_cacheLock)
        {
            if (_uidsCache.TryGetValue(folderId, out var cached))
                return cached;
        }
        var uids = _repo.GetUids(folderId);
        lock (_cacheLock)
        {
            _uidsCache[folderId] = uids;
        }
        return uids;
    }

    public string? GetBody(int folderId, uint uid) => _repo.GetMessageBody(folderId, uid);

    public void StoreBody(int folderId, uint uid, string body, List<AttachmentInfo>? attachments = null)
    {
        string? attachmentsJson = attachments != null
            ? System.Text.Json.JsonSerializer.Serialize(attachments)
            : null;
        _repo.StoreMessageBody(folderId, uid, body, attachmentsJson);
        InvalidateFolder(folderId);
    }

    public void UpdateFlags(int folderId, uint uid, bool isRead, bool isFlagged)
    {
        _repo.UpdateMessageFlags(folderId, uid, isRead, isFlagged);
        InvalidateFolder(folderId);
    }

    public void DeleteMessage(int folderId, uint uid)
    {
        _repo.DeleteMessage(folderId, uid);
        InvalidateFolder(folderId);
    }

    public void RestoreMessage(int folderId, MailMessage message)
    {
        message.FolderId = folderId;
        _repo.UpsertMessage(message);
        InvalidateFolder(folderId);
    }

    public void PurgeFolder(int folderId)
    {
        _repo.PurgeFolderMessages(folderId);
        InvalidateFolder(folderId);
    }

    public void InvalidateFolder(int folderId)
    {
        lock (_cacheLock)
        {
            _headersCache.Remove(folderId);
            _uidsCache.Remove(folderId);
        }
    }
}
