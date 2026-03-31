using CXPost.Data;
using CXPost.Models;
using CXPost.Services;
using Microsoft.Data.Sqlite;

namespace CXPost.Tests.Services;

public class CacheServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CacheService _service;

    public CacheServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DatabaseMigrations.Apply(_connection);
        var repo = new MailRepository(_connection);
        _service = new CacheService(repo);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void SyncFolders_stores_and_retrieves_folders()
    {
        var folders = new List<MailFolder>
        {
            new() { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox", UnreadCount = 5 },
            new() { AccountId = "acc1", Path = "Sent", DisplayName = "Sent" }
        };

        _service.SyncFolders("acc1", folders);
        var result = _service.GetFolders("acc1");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SyncHeaders_stores_messages_and_GetMessages_retrieves()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _service.SyncFolders("acc1", [folder]);
        var folderId = _service.GetFolders("acc1")[0].Id;

        var messages = new List<MailMessage>
        {
            new() { Uid = 1, Subject = "Test", FromAddress = "a@b.com", Date = DateTime.UtcNow }
        };

        _service.SyncHeaders(folderId, messages);
        var result = _service.GetMessages(folderId);

        Assert.Single(result);
        Assert.Equal("Test", result[0].Subject);
    }

    [Fact]
    public void GetCachedUids_returns_local_uid_set()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _service.SyncFolders("acc1", [folder]);
        var folderId = _service.GetFolders("acc1")[0].Id;

        _service.SyncHeaders(folderId, [
            new MailMessage { Uid = 10, Date = DateTime.UtcNow },
            new MailMessage { Uid = 20, Date = DateTime.UtcNow }
        ]);

        var uids = _service.GetCachedUids(folderId);

        Assert.Contains((uint)10, uids);
        Assert.Contains((uint)20, uids);
    }

    [Fact]
    public void PurgeFolder_removes_all_messages()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _service.SyncFolders("acc1", [folder]);
        var folderId = _service.GetFolders("acc1")[0].Id;

        _service.SyncHeaders(folderId, [
            new MailMessage { Uid = 1, Date = DateTime.UtcNow }
        ]);

        _service.PurgeFolder(folderId);

        Assert.Empty(_service.GetMessages(folderId));
    }
}
