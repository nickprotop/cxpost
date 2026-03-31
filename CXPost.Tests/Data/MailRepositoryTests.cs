using CXPost.Data;
using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Tests.Data;

public class MailRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MailRepository _repo;

    public MailRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DatabaseMigrations.Apply(_connection);
        _repo = new MailRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void UpsertFolder_and_GetFolders_returns_folder()
    {
        var folder = new MailFolder
        {
            AccountId = "acc1",
            Path = "INBOX",
            DisplayName = "Inbox",
            UidValidity = 12345,
            UnreadCount = 3,
            TotalCount = 50
        };

        _repo.UpsertFolder(folder);
        var folders = _repo.GetFolders("acc1");

        Assert.Single(folders);
        Assert.Equal("INBOX", folders[0].Path);
        Assert.Equal("Inbox", folders[0].DisplayName);
        Assert.Equal(3, folders[0].UnreadCount);
    }

    [Fact]
    public void UpsertMessage_and_GetMessages_returns_messages_ordered_by_date_desc()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        var msg1 = new MailMessage
        {
            FolderId = folderId, Uid = 1, Subject = "Old",
            FromAddress = "a@b.com", Date = new DateTime(2026, 1, 1)
        };
        var msg2 = new MailMessage
        {
            FolderId = folderId, Uid = 2, Subject = "New",
            FromAddress = "c@d.com", Date = new DateTime(2026, 3, 1)
        };

        _repo.UpsertMessage(msg1);
        _repo.UpsertMessage(msg2);
        var messages = _repo.GetMessages(folderId);

        Assert.Equal(2, messages.Count);
        Assert.Equal("New", messages[0].Subject);
        Assert.Equal("Old", messages[1].Subject);
    }

    [Fact]
    public void UpdateMessageFlags_updates_read_and_flagged()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        var msg = new MailMessage
        {
            FolderId = folderId, Uid = 1, Subject = "Test",
            FromAddress = "a@b.com", Date = DateTime.UtcNow
        };
        _repo.UpsertMessage(msg);

        _repo.UpdateMessageFlags(folderId, 1, isRead: true, isFlagged: true);
        var messages = _repo.GetMessages(folderId);

        Assert.True(messages[0].IsRead);
        Assert.True(messages[0].IsFlagged);
    }

    [Fact]
    public void DeleteMessage_removes_message()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        var msg = new MailMessage
        {
            FolderId = folderId, Uid = 1, Subject = "ToDelete",
            FromAddress = "a@b.com", Date = DateTime.UtcNow
        };
        _repo.UpsertMessage(msg);
        _repo.DeleteMessage(folderId, 1);

        Assert.Empty(_repo.GetMessages(folderId));
    }

    [Fact]
    public void GetMessageBody_returns_null_when_not_fetched()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        var msg = new MailMessage
        {
            FolderId = folderId, Uid = 1, Subject = "NoBody",
            FromAddress = "a@b.com", Date = DateTime.UtcNow
        };
        _repo.UpsertMessage(msg);
        var body = _repo.GetMessageBody(folderId, 1);

        Assert.Null(body);
    }

    [Fact]
    public void StoreMessageBody_and_GetMessageBody_returns_body()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        var msg = new MailMessage
        {
            FolderId = folderId, Uid = 1, Subject = "WithBody",
            FromAddress = "a@b.com", Date = DateTime.UtcNow
        };
        _repo.UpsertMessage(msg);
        _repo.StoreMessageBody(folderId, 1, "Hello, world!");
        var body = _repo.GetMessageBody(folderId, 1);

        Assert.Equal("Hello, world!", body);
    }

    [Fact]
    public void GetUids_returns_all_uids_for_folder()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        _repo.UpsertMessage(new MailMessage { FolderId = folderId, Uid = 10, Date = DateTime.UtcNow });
        _repo.UpsertMessage(new MailMessage { FolderId = folderId, Uid = 20, Date = DateTime.UtcNow });
        _repo.UpsertMessage(new MailMessage { FolderId = folderId, Uid = 30, Date = DateTime.UtcNow });

        var uids = _repo.GetUids(folderId);

        Assert.Equal(new uint[] { 10, 20, 30 }, uids.OrderBy(u => u));
    }

    [Fact]
    public void PurgeFolderMessages_removes_all_messages_for_folder()
    {
        var folder = new MailFolder { AccountId = "acc1", Path = "INBOX", DisplayName = "Inbox" };
        _repo.UpsertFolder(folder);
        var folderId = _repo.GetFolders("acc1")[0].Id;

        _repo.UpsertMessage(new MailMessage { FolderId = folderId, Uid = 1, Date = DateTime.UtcNow });
        _repo.UpsertMessage(new MailMessage { FolderId = folderId, Uid = 2, Date = DateTime.UtcNow });

        _repo.PurgeFolderMessages(folderId);

        Assert.Empty(_repo.GetMessages(folderId));
    }
}
