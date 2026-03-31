# CXPost Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cross-platform TUI mail client using ConsoleEx (SharpConsoleUI) and MailKit, with IMAP/SMTP support, offline cache, unified inbox, and conversation threading.

**Architecture:** Coordinator pattern with Microsoft.Extensions.DependencyInjection. Services handle protocol/data, coordinators orchestrate features, UI panels compose the 3-pane layout. Async sync engine with thread-safe UI update queue.

**Tech Stack:** .NET 10, SharpConsoleUI (ConsoleEx), MailKit/MimeKit, Microsoft.Extensions.DependencyInjection, Microsoft.Data.Sqlite, YamlDotNet

---

## File Structure

```
~/source/cxpost/
├── CXPost/
│   ├── Program.cs
│   ├── CXPost.csproj
│   ├── Models/
│   │   ├── Account.cs
│   │   ├── MailFolder.cs
│   │   ├── MailMessage.cs
│   │   ├── MailThread.cs
│   │   └── Contact.cs
│   ├── Services/
│   │   ├── IImapService.cs
│   │   ├── ImapService.cs
│   │   ├── ISmtpService.cs
│   │   ├── SmtpService.cs
│   │   ├── ICacheService.cs
│   │   ├── CacheService.cs
│   │   ├── IContactsService.cs
│   │   ├── ContactsService.cs
│   │   ├── IConfigService.cs
│   │   ├── ConfigService.cs
│   │   ├── ICredentialService.cs
│   │   ├── CredentialService.cs
│   │   └── ThreadingService.cs
│   ├── Coordinators/
│   │   ├── MailSyncCoordinator.cs
│   │   ├── FolderCoordinator.cs
│   │   ├── MessageListCoordinator.cs
│   │   ├── ComposeCoordinator.cs
│   │   ├── SearchCoordinator.cs
│   │   └── NotificationCoordinator.cs
│   ├── UI/
│   │   ├── CXPostApp.cs
│   │   ├── Panels/
│   │   │   ├── FolderPanel.cs
│   │   │   ├── MessageListPanel.cs
│   │   │   └── ReadingPanel.cs
│   │   ├── Dialogs/
│   │   │   ├── DialogBase.cs
│   │   │   ├── ComposeDialog.cs
│   │   │   ├── SettingsDialog.cs
│   │   │   ├── AccountSetupDialog.cs
│   │   │   ├── FolderPickerDialog.cs
│   │   │   └── SearchDialog.cs
│   │   ├── Components/
│   │   │   ├── StatusBarBuilder.cs
│   │   │   ├── MessageFormatter.cs
│   │   │   └── ColorScheme.cs
│   │   └── KeyBindings.cs
│   └── Data/
│       ├── MailRepository.cs
│       ├── ContactRepository.cs
│       └── DatabaseMigrations.cs
├── CXPost.Tests/
│   ├── CXPost.Tests.csproj
│   ├── Services/
│   │   ├── ThreadingServiceTests.cs
│   │   ├── CacheServiceTests.cs
│   │   ├── ContactsServiceTests.cs
│   │   └── ConfigServiceTests.cs
│   ├── Data/
│   │   ├── MailRepositoryTests.cs
│   │   └── ContactRepositoryTests.cs
│   └── Coordinators/
│       └── MessageListCoordinatorTests.cs
└── CXPost.sln
```

---

### Task 1: Project Scaffolding & Solution Setup

**Files:**
- Create: `~/source/cxpost/CXPost.sln`
- Create: `~/source/cxpost/CXPost/CXPost.csproj`
- Create: `~/source/cxpost/CXPost/Program.cs`
- Create: `~/source/cxpost/CXPost.Tests/CXPost.Tests.csproj`

- [ ] **Step 1: Initialize git repo**

```bash
cd ~/source/cxpost
git init
```

- [ ] **Step 2: Create the solution and projects**

```bash
cd ~/source/cxpost
dotnet new sln -n CXPost
dotnet new console -n CXPost -o CXPost --framework net10.0
dotnet new xunit -n CXPost.Tests -o CXPost.Tests --framework net10.0
dotnet sln add CXPost/CXPost.csproj
dotnet sln add CXPost.Tests/CXPost.Tests.csproj
dotnet add CXPost.Tests reference CXPost/CXPost.csproj
```

- [ ] **Step 3: Add NuGet dependencies to CXPost.csproj**

Edit `~/source/cxpost/CXPost/CXPost.csproj` to contain:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\ConsoleEx\SharpConsoleUI\SharpConsoleUI.csproj" />
    <PackageReference Include="MailKit" Version="4.*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="9.*" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.*" />
    <PackageReference Include="YamlDotNet" Version="16.*" />
  </ItemGroup>
</Project>
```

Note: SharpConsoleUI is referenced as a project reference from `~/source/ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj`. Adjust relative path if needed.

- [ ] **Step 4: Add test dependencies to CXPost.Tests.csproj**

Edit `~/source/cxpost/CXPost.Tests/CXPost.Tests.csproj` — ensure it has:

```xml
<ItemGroup>
  <PackageReference Include="NSubstitute" Version="5.*" />
</ItemGroup>
```

- [ ] **Step 5: Create minimal Program.cs**

Write `~/source/cxpost/CXPost/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace CXPost;

public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            if (width <= 0 || height <= 0)
            {
                Console.Error.WriteLine("CXPost requires an interactive terminal.");
                return 1;
            }
        }
        catch
        {
            Console.Error.WriteLine("CXPost requires an interactive terminal.");
            return 1;
        }

        // DI container will be configured here
        Console.WriteLine("CXPost - TUI Mail Client");
        return 0;
    }
}
```

- [ ] **Step 6: Create .gitignore**

Write `~/source/cxpost/.gitignore`:

```
bin/
obj/
.vs/
*.user
*.suo
.superpowers/
```

- [ ] **Step 7: Build and verify**

```bash
cd ~/source/cxpost
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 8: Run tests to verify test project**

```bash
cd ~/source/cxpost
dotnet test
```

Expected: default generated test passes.

- [ ] **Step 9: Commit**

```bash
cd ~/source/cxpost
git add -A
git commit -m "feat: scaffold CXPost solution with dependencies"
```

---

### Task 2: Models

**Files:**
- Create: `~/source/cxpost/CXPost/Models/Account.cs`
- Create: `~/source/cxpost/CXPost/Models/MailFolder.cs`
- Create: `~/source/cxpost/CXPost/Models/MailMessage.cs`
- Create: `~/source/cxpost/CXPost/Models/MailThread.cs`
- Create: `~/source/cxpost/CXPost/Models/Contact.cs`

- [ ] **Step 1: Create Account model**

Write `~/source/cxpost/CXPost/Models/Account.cs`:

```csharp
namespace CXPost.Models;

public class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public SecurityType ImapSecurity { get; set; } = SecurityType.Ssl;
    public SecurityType SmtpSecurity { get; set; } = SecurityType.StartTls;
    public DateTime? LastSync { get; set; }
}

public enum SecurityType
{
    None,
    Ssl,
    StartTls
}
```

- [ ] **Step 2: Create MailFolder model**

Write `~/source/cxpost/CXPost/Models/MailFolder.cs`:

```csharp
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
```

- [ ] **Step 3: Create MailMessage model**

Write `~/source/cxpost/CXPost/Models/MailMessage.cs`:

```csharp
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
}
```

- [ ] **Step 4: Create MailThread model**

Write `~/source/cxpost/CXPost/Models/MailThread.cs`:

```csharp
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
```

- [ ] **Step 5: Create Contact model**

Write `~/source/cxpost/CXPost/Models/Contact.cs`:

```csharp
namespace CXPost.Models;

public class Contact
{
    public string Address { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int UseCount { get; set; } = 1;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 6: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Models/
git commit -m "feat: add domain models (Account, MailFolder, MailMessage, MailThread, Contact)"
```

---

### Task 3: Database Layer

**Files:**
- Create: `~/source/cxpost/CXPost/Data/DatabaseMigrations.cs`
- Create: `~/source/cxpost/CXPost/Data/MailRepository.cs`
- Create: `~/source/cxpost/CXPost/Data/ContactRepository.cs`
- Test: `~/source/cxpost/CXPost.Tests/Data/MailRepositoryTests.cs`
- Test: `~/source/cxpost/CXPost.Tests/Data/ContactRepositoryTests.cs`

- [ ] **Step 1: Write MailRepository tests**

Write `~/source/cxpost/CXPost.Tests/Data/MailRepositoryTests.cs`:

```csharp
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
```

- [ ] **Step 2: Write ContactRepository tests**

Write `~/source/cxpost/CXPost.Tests/Data/ContactRepositoryTests.cs`:

```csharp
using CXPost.Data;
using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Tests.Data;

public class ContactRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ContactRepository _repo;

    public ContactRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DatabaseMigrations.ApplyContacts(_connection);
        _repo = new ContactRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void UpsertContact_creates_new_contact()
    {
        _repo.UpsertContact("alice@example.com", "Alice");
        var results = _repo.Search("alice");

        Assert.Single(results);
        Assert.Equal("alice@example.com", results[0].Address);
        Assert.Equal("Alice", results[0].DisplayName);
    }

    [Fact]
    public void UpsertContact_increments_use_count_on_duplicate()
    {
        _repo.UpsertContact("alice@example.com", "Alice");
        _repo.UpsertContact("alice@example.com", "Alice");
        var results = _repo.Search("alice");

        Assert.Single(results);
        Assert.Equal(2, results[0].UseCount);
    }

    [Fact]
    public void Search_returns_results_ordered_by_use_count_desc()
    {
        _repo.UpsertContact("rare@example.com", "Rare");
        _repo.UpsertContact("frequent@example.com", "Frequent");
        _repo.UpsertContact("frequent@example.com", "Frequent");
        _repo.UpsertContact("frequent@example.com", "Frequent");

        var results = _repo.Search("example.com");

        Assert.Equal(2, results.Count);
        Assert.Equal("frequent@example.com", results[0].Address);
    }

    [Fact]
    public void Search_matches_display_name_and_address()
    {
        _repo.UpsertContact("bob@work.com", "Bob Smith");

        Assert.Single(_repo.Search("bob"));
        Assert.Single(_repo.Search("work.com"));
        Assert.Single(_repo.Search("Smith"));
    }

    [Fact]
    public void Search_returns_empty_for_no_match()
    {
        _repo.UpsertContact("alice@example.com", "Alice");
        Assert.Empty(_repo.Search("nobody"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: Compilation errors — `DatabaseMigrations`, `MailRepository`, `ContactRepository` don't exist yet.

- [ ] **Step 4: Implement DatabaseMigrations**

Write `~/source/cxpost/CXPost/Data/DatabaseMigrations.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace CXPost.Data;

public static class DatabaseMigrations
{
    public static void Apply(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NOT NULL,
                path TEXT NOT NULL,
                display_name TEXT NOT NULL,
                uidvalidity INTEGER NOT NULL DEFAULT 0,
                unread_count INTEGER NOT NULL DEFAULT 0,
                total_count INTEGER NOT NULL DEFAULT 0,
                UNIQUE(account_id, path)
            );

            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                folder_id INTEGER NOT NULL REFERENCES folders(id),
                uid INTEGER NOT NULL,
                message_id TEXT,
                in_reply_to TEXT,
                "references" TEXT,
                thread_id TEXT,
                from_name TEXT,
                from_address TEXT,
                to_addresses TEXT,
                cc_addresses TEXT,
                subject TEXT,
                date TEXT NOT NULL,
                is_read INTEGER NOT NULL DEFAULT 0,
                is_flagged INTEGER NOT NULL DEFAULT 0,
                has_attachments INTEGER NOT NULL DEFAULT 0,
                body_plain TEXT,
                body_fetched INTEGER NOT NULL DEFAULT 0,
                UNIQUE(folder_id, uid)
            );

            CREATE INDEX IF NOT EXISTS idx_messages_thread ON messages(thread_id);
            CREATE INDEX IF NOT EXISTS idx_messages_date ON messages(folder_id, date DESC);
            CREATE INDEX IF NOT EXISTS idx_messages_from ON messages(from_address);
            """;
        cmd.ExecuteNonQuery();
    }

    public static void ApplyContacts(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS contacts (
                address TEXT PRIMARY KEY,
                display_name TEXT,
                use_count INTEGER NOT NULL DEFAULT 1,
                last_used TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
```

- [ ] **Step 5: Implement MailRepository**

Write `~/source/cxpost/CXPost/Data/MailRepository.cs`:

```csharp
using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Data;

public class MailRepository
{
    private readonly SqliteConnection _connection;

    public MailRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public void UpsertFolder(MailFolder folder)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO folders (account_id, path, display_name, uidvalidity, unread_count, total_count)
            VALUES (@accountId, @path, @displayName, @uidvalidity, @unreadCount, @totalCount)
            ON CONFLICT(account_id, path) DO UPDATE SET
                display_name = @displayName,
                uidvalidity = @uidvalidity,
                unread_count = @unreadCount,
                total_count = @totalCount
            """;
        cmd.Parameters.AddWithValue("@accountId", folder.AccountId);
        cmd.Parameters.AddWithValue("@path", folder.Path);
        cmd.Parameters.AddWithValue("@displayName", folder.DisplayName);
        cmd.Parameters.AddWithValue("@uidvalidity", folder.UidValidity);
        cmd.Parameters.AddWithValue("@unreadCount", folder.UnreadCount);
        cmd.Parameters.AddWithValue("@totalCount", folder.TotalCount);
        cmd.ExecuteNonQuery();
    }

    public List<MailFolder> GetFolders(string accountId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, account_id, path, display_name, uidvalidity, unread_count, total_count FROM folders WHERE account_id = @accountId";
        cmd.Parameters.AddWithValue("@accountId", accountId);

        var folders = new List<MailFolder>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new MailFolder
            {
                Id = reader.GetInt32(0),
                AccountId = reader.GetString(1),
                Path = reader.GetString(2),
                DisplayName = reader.GetString(3),
                UidValidity = (uint)reader.GetInt64(4),
                UnreadCount = reader.GetInt32(5),
                TotalCount = reader.GetInt32(6)
            });
        }
        return folders;
    }

    public void UpsertMessage(MailMessage msg)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO messages (folder_id, uid, message_id, in_reply_to, "references", thread_id,
                from_name, from_address, to_addresses, cc_addresses, subject, date,
                is_read, is_flagged, has_attachments, body_plain, body_fetched)
            VALUES (@folderId, @uid, @messageId, @inReplyTo, @references, @threadId,
                @fromName, @fromAddress, @toAddresses, @ccAddresses, @subject, @date,
                @isRead, @isFlagged, @hasAttachments, @bodyPlain, @bodyFetched)
            ON CONFLICT(folder_id, uid) DO UPDATE SET
                message_id = @messageId, in_reply_to = @inReplyTo, "references" = @references,
                thread_id = @threadId, from_name = @fromName, from_address = @fromAddress,
                to_addresses = @toAddresses, cc_addresses = @ccAddresses, subject = @subject,
                date = @date, is_read = @isRead, is_flagged = @isFlagged,
                has_attachments = @hasAttachments
            """;
        cmd.Parameters.AddWithValue("@folderId", msg.FolderId);
        cmd.Parameters.AddWithValue("@uid", (long)msg.Uid);
        cmd.Parameters.AddWithValue("@messageId", (object?)msg.MessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inReplyTo", (object?)msg.InReplyTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@references", (object?)msg.References ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@threadId", (object?)msg.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromName", (object?)msg.FromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromAddress", (object?)msg.FromAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@toAddresses", (object?)msg.ToAddresses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ccAddresses", (object?)msg.CcAddresses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)msg.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date", msg.Date.ToString("O"));
        cmd.Parameters.AddWithValue("@isRead", msg.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@isFlagged", msg.IsFlagged ? 1 : 0);
        cmd.Parameters.AddWithValue("@hasAttachments", msg.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@bodyPlain", (object?)msg.BodyPlain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyFetched", msg.BodyFetched ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public List<MailMessage> GetMessages(int folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, folder_id, uid, message_id, in_reply_to, "references", thread_id,
                from_name, from_address, to_addresses, cc_addresses, subject, date,
                is_read, is_flagged, has_attachments, body_plain, body_fetched
            FROM messages WHERE folder_id = @folderId ORDER BY date DESC
            """;
        cmd.Parameters.AddWithValue("@folderId", folderId);

        var messages = new List<MailMessage>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(ReadMessage(reader));
        }
        return messages;
    }

    public void UpdateMessageFlags(int folderId, uint uid, bool isRead, bool isFlagged)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE messages SET is_read = @isRead, is_flagged = @isFlagged WHERE folder_id = @folderId AND uid = @uid";
        cmd.Parameters.AddWithValue("@isRead", isRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@isFlagged", isFlagged ? 1 : 0);
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        cmd.ExecuteNonQuery();
    }

    public void DeleteMessage(int folderId, uint uid)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE folder_id = @folderId AND uid = @uid";
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        cmd.ExecuteNonQuery();
    }

    public string? GetMessageBody(int folderId, uint uid)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT body_plain FROM messages WHERE folder_id = @folderId AND uid = @uid AND body_fetched = 1";
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        return cmd.ExecuteScalar() as string;
    }

    public void StoreMessageBody(int folderId, uint uid, string body)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE messages SET body_plain = @body, body_fetched = 1 WHERE folder_id = @folderId AND uid = @uid";
        cmd.Parameters.AddWithValue("@body", body);
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        cmd.ExecuteNonQuery();
    }

    public HashSet<uint> GetUids(int folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT uid FROM messages WHERE folder_id = @folderId";
        cmd.Parameters.AddWithValue("@folderId", folderId);

        var uids = new HashSet<uint>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            uids.Add((uint)reader.GetInt64(0));
        return uids;
    }

    public void PurgeFolderMessages(int folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE folder_id = @folderId";
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.ExecuteNonQuery();
    }

    private static MailMessage ReadMessage(SqliteDataReader reader)
    {
        return new MailMessage
        {
            Id = reader.GetInt32(0),
            FolderId = reader.GetInt32(1),
            Uid = (uint)reader.GetInt64(2),
            MessageId = reader.IsDBNull(3) ? null : reader.GetString(3),
            InReplyTo = reader.IsDBNull(4) ? null : reader.GetString(4),
            References = reader.IsDBNull(5) ? null : reader.GetString(5),
            ThreadId = reader.IsDBNull(6) ? null : reader.GetString(6),
            FromName = reader.IsDBNull(7) ? null : reader.GetString(7),
            FromAddress = reader.IsDBNull(8) ? null : reader.GetString(8),
            ToAddresses = reader.IsDBNull(9) ? null : reader.GetString(9),
            CcAddresses = reader.IsDBNull(10) ? null : reader.GetString(10),
            Subject = reader.IsDBNull(11) ? null : reader.GetString(11),
            Date = DateTime.Parse(reader.GetString(12)),
            IsRead = reader.GetInt32(13) != 0,
            IsFlagged = reader.GetInt32(14) != 0,
            HasAttachments = reader.GetInt32(15) != 0,
            BodyPlain = reader.IsDBNull(16) ? null : reader.GetString(16),
            BodyFetched = reader.GetInt32(17) != 0
        };
    }
}
```

- [ ] **Step 6: Implement ContactRepository**

Write `~/source/cxpost/CXPost/Data/ContactRepository.cs`:

```csharp
using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Data;

public class ContactRepository
{
    private readonly SqliteConnection _connection;

    public ContactRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public void UpsertContact(string address, string? displayName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO contacts (address, display_name, use_count, last_used)
            VALUES (@address, @displayName, 1, @lastUsed)
            ON CONFLICT(address) DO UPDATE SET
                display_name = COALESCE(@displayName, display_name),
                use_count = use_count + 1,
                last_used = @lastUsed
            """;
        cmd.Parameters.AddWithValue("@address", address);
        cmd.Parameters.AddWithValue("@displayName", (object?)displayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastUsed", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<Contact> Search(string query)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT address, display_name, use_count, last_used
            FROM contacts
            WHERE address LIKE @query OR display_name LIKE @query
            ORDER BY use_count DESC
            LIMIT 20
            """;
        cmd.Parameters.AddWithValue("@query", $"%{query}%");

        var contacts = new List<Contact>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            contacts.Add(new Contact
            {
                Address = reader.GetString(0),
                DisplayName = reader.IsDBNull(1) ? null : reader.GetString(1),
                UseCount = reader.GetInt32(2),
                LastUsed = DateTime.Parse(reader.GetString(3))
            });
        }
        return contacts;
    }
}
```

- [ ] **Step 7: Run tests**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Data/ CXPost.Tests/Data/
git commit -m "feat: add database layer with MailRepository and ContactRepository"
```

---

### Task 4: Configuration Service

**Files:**
- Create: `~/source/cxpost/CXPost/Services/IConfigService.cs`
- Create: `~/source/cxpost/CXPost/Services/ConfigService.cs`
- Test: `~/source/cxpost/CXPost.Tests/Services/ConfigServiceTests.cs`

- [ ] **Step 1: Write ConfigService tests**

Write `~/source/cxpost/CXPost.Tests/Services/ConfigServiceTests.cs`:

```csharp
using CXPost.Models;
using CXPost.Services;

namespace CXPost.Tests.Services;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cxpost-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new ConfigService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_returns_empty_config_when_no_file_exists()
    {
        var config = _service.Load();

        Assert.NotNull(config);
        Assert.Empty(config.Accounts);
        Assert.Equal("classic", config.Layout);
    }

    [Fact]
    public void Save_and_Load_roundtrips_config()
    {
        var config = new CXPostConfig
        {
            Layout = "vertical",
            SyncIntervalSeconds = 120,
            Notifications = false,
            Accounts =
            [
                new Account
                {
                    Name = "Test",
                    Email = "test@example.com",
                    ImapHost = "imap.example.com",
                    ImapPort = 993,
                    SmtpHost = "smtp.example.com",
                    SmtpPort = 587,
                    Signature = "-- \nSent from CXPost"
                }
            ]
        };

        _service.Save(config);
        var loaded = _service.Load();

        Assert.Equal("vertical", loaded.Layout);
        Assert.Equal(120, loaded.SyncIntervalSeconds);
        Assert.False(loaded.Notifications);
        Assert.Single(loaded.Accounts);
        Assert.Equal("test@example.com", loaded.Accounts[0].Email);
        Assert.Equal("imap.example.com", loaded.Accounts[0].ImapHost);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: Compilation errors — `CXPostConfig`, `IConfigService`, `ConfigService` don't exist.

- [ ] **Step 3: Create CXPostConfig model**

Add to `~/source/cxpost/CXPost/Models/Account.cs` (append to file):

```csharp
public class CXPostConfig
{
    public List<Account> Accounts { get; set; } = [];
    public string Layout { get; set; } = "classic";
    public int SyncIntervalSeconds { get; set; } = 300;
    public bool Notifications { get; set; } = true;
}
```

- [ ] **Step 4: Implement IConfigService and ConfigService**

Write `~/source/cxpost/CXPost/Services/IConfigService.cs`:

```csharp
using CXPost.Models;

namespace CXPost.Services;

public interface IConfigService
{
    CXPostConfig Load();
    void Save(CXPostConfig config);
}
```

Write `~/source/cxpost/CXPost/Services/ConfigService.cs`:

```csharp
using CXPost.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CXPost.Services;

public class ConfigService : IConfigService
{
    private readonly string _configPath;

    public ConfigService(string configDir)
    {
        _configPath = Path.Combine(configDir, "config.yaml");
    }

    public CXPostConfig Load()
    {
        if (!File.Exists(_configPath))
            return new CXPostConfig();

        var yaml = File.ReadAllText(_configPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<CXPostConfig>(yaml) ?? new CXPostConfig();
    }

    public void Save(CXPostConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        File.WriteAllText(_configPath, serializer.Serialize(config));
    }
}
```

- [ ] **Step 5: Run tests**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Services/IConfigService.cs CXPost/Services/ConfigService.cs CXPost/Models/Account.cs CXPost.Tests/Services/ConfigServiceTests.cs
git commit -m "feat: add YAML configuration service"
```

---

### Task 5: Threading Service (JWZ Algorithm)

**Files:**
- Create: `~/source/cxpost/CXPost/Services/ThreadingService.cs`
- Test: `~/source/cxpost/CXPost.Tests/Services/ThreadingServiceTests.cs`

- [ ] **Step 1: Write ThreadingService tests**

Write `~/source/cxpost/CXPost.Tests/Services/ThreadingServiceTests.cs`:

```csharp
using CXPost.Models;
using CXPost.Services;

namespace CXPost.Tests.Services;

public class ThreadingServiceTests
{
    private readonly ThreadingService _service = new();

    [Fact]
    public void AssignThreadIds_groups_reply_chain_by_message_id()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<msg1@ex>", Subject = "Hello" },
            new() { Id = 2, MessageId = "<msg2@ex>", InReplyTo = "<msg1@ex>", Subject = "Re: Hello" },
            new() { Id = 3, MessageId = "<msg3@ex>", InReplyTo = "<msg2@ex>", Subject = "Re: Re: Hello" }
        };

        _service.AssignThreadIds(messages);

        Assert.NotNull(messages[0].ThreadId);
        Assert.Equal(messages[0].ThreadId, messages[1].ThreadId);
        Assert.Equal(messages[0].ThreadId, messages[2].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_uses_references_header()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<root@ex>", Subject = "Thread" },
            new() { Id = 2, MessageId = "<reply@ex>", References = "<root@ex>", Subject = "Re: Thread" }
        };

        _service.AssignThreadIds(messages);

        Assert.Equal(messages[0].ThreadId, messages[1].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_separate_threads_get_different_ids()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<a@ex>", Subject = "Topic A" },
            new() { Id = 2, MessageId = "<b@ex>", Subject = "Topic B" }
        };

        _service.AssignThreadIds(messages);

        Assert.NotEqual(messages[0].ThreadId, messages[1].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_handles_missing_message_id()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, Subject = "No message ID" }
        };

        _service.AssignThreadIds(messages);

        Assert.NotNull(messages[0].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_chains_through_references_list()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<a@ex>", Subject = "Start" },
            new() { Id = 2, MessageId = "<b@ex>", InReplyTo = "<a@ex>", References = "<a@ex>", Subject = "Re: Start" },
            new() { Id = 3, MessageId = "<c@ex>", InReplyTo = "<b@ex>", References = "<a@ex> <b@ex>", Subject = "Re: Re: Start" }
        };

        _service.AssignThreadIds(messages);

        var threadId = messages[0].ThreadId;
        Assert.All(messages, m => Assert.Equal(threadId, m.ThreadId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: Compilation error — `ThreadingService` doesn't exist.

- [ ] **Step 3: Implement ThreadingService**

Write `~/source/cxpost/CXPost/Services/ThreadingService.cs`:

```csharp
using CXPost.Models;

namespace CXPost.Services;

/// <summary>
/// Simplified JWZ threading: groups messages into threads using
/// Message-ID, In-Reply-To, and References headers.
/// Uses union-find to merge related message IDs into thread groups.
/// </summary>
public class ThreadingService
{
    public void AssignThreadIds(List<MailMessage> messages)
    {
        // Union-Find: maps message-id -> root message-id
        var parent = new Dictionary<string, string>();

        string Find(string id)
        {
            if (!parent.ContainsKey(id))
                parent[id] = id;
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]]; // path compression
                id = parent[id];
            }
            return id;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[ra] = rb;
        }

        // Build unions from message relationships
        foreach (var msg in messages)
        {
            var msgId = msg.MessageId;
            if (string.IsNullOrEmpty(msgId))
                continue;

            Find(msgId); // ensure registered

            // Link via In-Reply-To
            if (!string.IsNullOrEmpty(msg.InReplyTo))
                Union(msgId, msg.InReplyTo);

            // Link via References (space-separated message IDs)
            if (!string.IsNullOrEmpty(msg.References))
            {
                var refs = msg.References.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var refId in refs)
                    Union(msgId, refId);

                // Also chain consecutive references
                for (var i = 1; i < refs.Length; i++)
                    Union(refs[i - 1], refs[i]);
            }
        }

        // Map roots to stable thread IDs
        var rootToThreadId = new Dictionary<string, string>();

        foreach (var msg in messages)
        {
            if (!string.IsNullOrEmpty(msg.MessageId))
            {
                var root = Find(msg.MessageId);
                if (!rootToThreadId.ContainsKey(root))
                    rootToThreadId[root] = root; // use root message-id as thread id
                msg.ThreadId = rootToThreadId[root];
            }
            else
            {
                // No message ID — standalone thread
                msg.ThreadId = $"<orphan-{msg.Id}>";
            }
        }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Services/ThreadingService.cs CXPost.Tests/Services/ThreadingServiceTests.cs
git commit -m "feat: add JWZ-based message threading service"
```

---

### Task 6: IMAP & SMTP Service Interfaces

**Files:**
- Create: `~/source/cxpost/CXPost/Services/IImapService.cs`
- Create: `~/source/cxpost/CXPost/Services/ImapService.cs`
- Create: `~/source/cxpost/CXPost/Services/ISmtpService.cs`
- Create: `~/source/cxpost/CXPost/Services/SmtpService.cs`
- Create: `~/source/cxpost/CXPost/Services/ICredentialService.cs`
- Create: `~/source/cxpost/CXPost/Services/CredentialService.cs`

- [ ] **Step 1: Create IImapService interface**

Write `~/source/cxpost/CXPost/Services/IImapService.cs`:

```csharp
using CXPost.Models;

namespace CXPost.Services;

public interface IImapService
{
    Task ConnectAsync(Account account, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    Task<List<MailFolder>> GetFoldersAsync(CancellationToken ct = default);
    Task<List<MailMessage>> FetchHeadersAsync(string folderPath, uint? sinceUid = null, CancellationToken ct = default);
    Task<string?> FetchBodyAsync(string folderPath, uint uid, CancellationToken ct = default);
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
}
```

- [ ] **Step 2: Create ISmtpService interface**

Write `~/source/cxpost/CXPost/Services/ISmtpService.cs`:

```csharp
using CXPost.Models;

namespace CXPost.Services;

public interface ISmtpService
{
    bool IsConnected { get; }
    Task ConnectAsync(Account account, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendAsync(MimeKit.MimeMessage message, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create ICredentialService interface**

Write `~/source/cxpost/CXPost/Services/ICredentialService.cs`:

```csharp
namespace CXPost.Services;

public interface ICredentialService
{
    string? GetPassword(string accountId);
    void StorePassword(string accountId, string password);
    void DeletePassword(string accountId);
}
```

- [ ] **Step 4: Implement CredentialService (CLI-based for cross-platform)**

Write `~/source/cxpost/CXPost/Services/CredentialService.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CXPost.Services;

/// <summary>
/// Cross-platform credential storage using OS CLI tools:
/// - Linux: secret-tool (libsecret)
/// - macOS: security (Keychain)
/// - Windows: cmdkey / credential manager via PowerShell
/// </summary>
public class CredentialService : ICredentialService
{
    private const string ServiceName = "cxpost";

    public string? GetPassword(string accountId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RunProcess("secret-tool", $"lookup service {ServiceName} account {accountId}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RunProcess("security", $"find-generic-password -s {ServiceName} -a {accountId} -w");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RunProcess("powershell", $"-Command \"(Get-StoredCredential -Target '{ServiceName}:{accountId}').GetNetworkCredential().Password\"");

        return null;
    }

    public void StorePassword(string accountId, string password)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            RunProcessWithInput("secret-tool", $"store --label=CXPost service {ServiceName} account {accountId}", password);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            RunProcess("security", $"add-generic-password -U -s {ServiceName} -a {accountId} -w {password}");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RunProcess("cmdkey", $"/generic:{ServiceName}:{accountId} /user:{accountId} /pass:{password}");
    }

    public void DeletePassword(string accountId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            RunProcess("secret-tool", $"clear service {ServiceName} account {accountId}");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            RunProcess("security", $"delete-generic-password -s {ServiceName} -a {accountId}");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RunProcess("cmdkey", $"/delete:{ServiceName}:{accountId}");
    }

    private static string? RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RunProcessWithInput(string fileName, string arguments, string input)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return;
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            process.WaitForExit(5000);
        }
        catch
        {
            // Silently fail — credential storage is best-effort
        }
    }
}
```

- [ ] **Step 5: Implement ImapService**

Write `~/source/cxpost/CXPost/Services/ImapService.cs`:

```csharp
using CXPost.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace CXPost.Services;

public class ImapService : IImapService, IDisposable
{
    private readonly ICredentialService _credentials;
    private ImapClient? _client;
    private Account? _account;

    public ImapService(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public bool IsConnected => _client?.IsConnected == true && _client.IsAuthenticated;

    public async Task ConnectAsync(Account account, CancellationToken ct = default)
    {
        _account = account;
        _client = new ImapClient();

        var useSsl = account.ImapSecurity == SecurityType.Ssl;
        await _client.ConnectAsync(account.ImapHost, account.ImapPort, useSsl, ct);

        if (account.ImapSecurity == SecurityType.StartTls)
            await _client.StartTlsAsync(ct);

        var password = _credentials.GetPassword(account.Id) ?? string.Empty;
        await _client.AuthenticateAsync(account.Username.Length > 0 ? account.Username : account.Email, password, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client is { IsConnected: true })
            await _client.DisconnectAsync(true, ct);
    }

    public async Task<List<Models.MailFolder>> GetFoldersAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var ns = _client!.PersonalNamespaces[0];
        var imapFolders = await _client.GetFoldersAsync(ns, cancellationToken: ct);

        var result = new List<Models.MailFolder>();
        foreach (var f in imapFolders)
        {
            if (!f.Exists) continue;
            try
            {
                await f.OpenAsync(FolderAccess.ReadOnly, ct);
                result.Add(new Models.MailFolder
                {
                    AccountId = _account!.Id,
                    Path = f.FullName,
                    DisplayName = f.Name,
                    UidValidity = f.UidValidity,
                    UnreadCount = f.Unread,
                    TotalCount = f.Count
                });
                await f.CloseAsync(false, ct);
            }
            catch (Exception)
            {
                // Skip folders we can't open (e.g. \Noselect)
            }
        }
        return result;
    }

    public async Task<List<Models.MailMessage>> FetchHeadersAsync(string folderPath, uint? sinceUid = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        IList<IMessageSummary> summaries;
        if (sinceUid.HasValue)
        {
            var range = new UniqueIdRange(new UniqueId(sinceUid.Value), UniqueId.MaxValue);
            summaries = await folder.FetchAsync(range,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                MessageSummaryItems.BodyStructure | MessageSummaryItems.References, ct);
        }
        else
        {
            summaries = await folder.FetchAsync(0, -1,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                MessageSummaryItems.BodyStructure | MessageSummaryItems.References, ct);
        }

        var messages = new List<Models.MailMessage>();
        foreach (var s in summaries)
        {
            var msg = new Models.MailMessage
            {
                Uid = s.UniqueId.Id,
                MessageId = s.Envelope.MessageId,
                InReplyTo = s.Envelope.InReplyTo,
                References = s.References != null ? string.Join(" ", s.References.Select(r => r.ToString())) : null,
                FromName = s.Envelope.From.Mailboxes.FirstOrDefault()?.Name,
                FromAddress = s.Envelope.From.Mailboxes.FirstOrDefault()?.Address,
                ToAddresses = System.Text.Json.JsonSerializer.Serialize(
                    s.Envelope.To.Mailboxes.Select(m => m.Address).ToList()),
                CcAddresses = s.Envelope.Cc.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(s.Envelope.Cc.Mailboxes.Select(m => m.Address).ToList())
                    : null,
                Subject = s.Envelope.Subject,
                Date = s.Envelope.Date?.UtcDateTime ?? DateTime.UtcNow,
                IsRead = s.Flags?.HasFlag(MessageFlags.Seen) == true,
                IsFlagged = s.Flags?.HasFlag(MessageFlags.Flagged) == true,
                HasAttachments = s.Body is MailKit.BodyPartMultipart multi && multi.BodyParts.Any(p => p is MailKit.BodyPartBasic b && b.IsAttachment)
            };
            messages.Add(msg);
        }

        await folder.CloseAsync(false, ct);
        return messages;
    }

    public async Task<string?> FetchBodyAsync(string folderPath, uint uid, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
        await folder.CloseAsync(false, ct);

        return message.TextBody ?? message.HtmlBody;
    }

    public async Task SetFlagsAsync(string folderPath, uint uid, bool? isRead = null, bool? isFlagged = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var uniqueId = new UniqueId(uid);
        if (isRead.HasValue)
        {
            if (isRead.Value)
                await folder.AddFlagsAsync(uniqueId, MessageFlags.Seen, true, ct);
            else
                await folder.RemoveFlagsAsync(uniqueId, MessageFlags.Seen, true, ct);
        }
        if (isFlagged.HasValue)
        {
            if (isFlagged.Value)
                await folder.AddFlagsAsync(uniqueId, MessageFlags.Flagged, true, ct);
            else
                await folder.RemoveFlagsAsync(uniqueId, MessageFlags.Flagged, true, ct);
        }

        await folder.CloseAsync(false, ct);
    }

    public async Task MoveMessageAsync(string sourcePath, string destPath, uint uid, CancellationToken ct = default)
    {
        EnsureConnected();
        var source = await _client!.GetFolderAsync(sourcePath, ct);
        var dest = await _client.GetFolderAsync(destPath, ct);
        await source.OpenAsync(FolderAccess.ReadWrite, ct);
        await source.MoveToAsync(new UniqueId(uid), dest, ct);
        await source.CloseAsync(false, ct);
    }

    public async Task DeleteMessageAsync(string folderPath, uint uid, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, ct);
        await folder.ExpungeAsync(ct);
        await folder.CloseAsync(false, ct);
    }

    public async Task<uint> AppendMessageAsync(string folderPath, MimeMessage message, MessageFlags flags = MessageFlags.Seen, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        var uid = await folder.AppendAsync(message, flags, ct);
        await folder.CloseAsync(false, ct);
        return uid?.Id ?? 0;
    }

    public async Task CreateFolderAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var ns = _client!.PersonalNamespaces[0];
        var topLevel = _client.GetFolder(ns);
        await topLevel.CreateAsync(folderPath, true, ct);
    }

    public async Task RenameFolderAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(oldPath, ct);
        var ns = _client.PersonalNamespaces[0];
        var parent = _client.GetFolder(ns);
        await folder.RenameAsync(parent, newPath, ct);
    }

    public async Task DeleteFolderAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.DeleteAsync(ct);
    }

    public async Task<List<uint>> SearchAsync(string folderPath, string query, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var searchQuery = SearchQuery.SubjectContains(query)
            .Or(SearchQuery.FromContains(query))
            .Or(SearchQuery.BodyContains(query));

        var results = await folder.SearchAsync(searchQuery, ct);
        await folder.CloseAsync(false, ct);
        return results.Select(uid => uid.Id).ToList();
    }

    public async Task IdleAsync(string folderPath, Action onNewMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        folder.CountChanged += (_, _) => onNewMessage();

        using var done = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, done.Token);

        // Re-IDLE every 29 minutes (IMAP spec)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(29), ct);
                done.Cancel();
            }
            catch (OperationCanceledException) { }
        }, ct);

        try
        {
            await _client!.IdleAsync(linked.Token);
        }
        catch (OperationCanceledException) { }

        await folder.CloseAsync(false, CancellationToken.None);
    }

    public async Task<uint> GetUidValidityAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var validity = folder.UidValidity;
        await folder.CloseAsync(false, ct);
        return validity;
    }

    public async Task<HashSet<uint>> GetUidsAsync(string folderPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var folder = await _client!.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var all = await folder.SearchAsync(SearchQuery.All, ct);
        await folder.CloseAsync(false, ct);
        return all.Select(u => u.Id).ToHashSet();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to IMAP server. Call ConnectAsync first.");
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
```

- [ ] **Step 6: Implement SmtpService**

Write `~/source/cxpost/CXPost/Services/SmtpService.cs`:

```csharp
using CXPost.Models;
using MailKit.Net.Smtp;
using MimeKit;

namespace CXPost.Services;

public class SmtpService : ISmtpService, IDisposable
{
    private readonly ICredentialService _credentials;
    private SmtpClient? _client;

    public SmtpService(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public bool IsConnected => _client?.IsConnected == true && _client.IsAuthenticated;

    public async Task ConnectAsync(Account account, CancellationToken ct = default)
    {
        _client = new SmtpClient();
        var useSsl = account.SmtpSecurity == SecurityType.Ssl;
        await _client.ConnectAsync(account.SmtpHost, account.SmtpPort, useSsl, ct);

        if (account.SmtpSecurity == SecurityType.StartTls)
            await _client.StartTlsAsync(ct);

        var password = _credentials.GetPassword(account.Id) ?? string.Empty;
        await _client.AuthenticateAsync(account.Username.Length > 0 ? account.Username : account.Email, password, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client is { IsConnected: true })
            await _client.DisconnectAsync(true, ct);
    }

    public async Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        if (_client is not { IsConnected: true })
            throw new InvalidOperationException("Not connected to SMTP server. Call ConnectAsync first.");

        await _client.SendAsync(message, ct);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
```

- [ ] **Step 7: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Services/
git commit -m "feat: add IMAP, SMTP, and credential services"
```

---

### Task 7: Cache Service

**Files:**
- Create: `~/source/cxpost/CXPost/Services/ICacheService.cs`
- Create: `~/source/cxpost/CXPost/Services/CacheService.cs`
- Test: `~/source/cxpost/CXPost.Tests/Services/CacheServiceTests.cs`

- [ ] **Step 1: Write CacheService tests**

Write `~/source/cxpost/CXPost.Tests/Services/CacheServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: Compilation errors.

- [ ] **Step 3: Implement ICacheService and CacheService**

Write `~/source/cxpost/CXPost/Services/ICacheService.cs`:

```csharp
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
```

Write `~/source/cxpost/CXPost/Services/CacheService.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Services/ICacheService.cs CXPost/Services/CacheService.cs CXPost.Tests/Services/CacheServiceTests.cs
git commit -m "feat: add cache service wrapping MailRepository"
```

---

### Task 8: Contacts Service

**Files:**
- Create: `~/source/cxpost/CXPost/Services/IContactsService.cs`
- Create: `~/source/cxpost/CXPost/Services/ContactsService.cs`
- Test: `~/source/cxpost/CXPost.Tests/Services/ContactsServiceTests.cs`

- [ ] **Step 1: Write ContactsService tests**

Write `~/source/cxpost/CXPost.Tests/Services/ContactsServiceTests.cs`:

```csharp
using CXPost.Data;
using CXPost.Services;
using Microsoft.Data.Sqlite;

namespace CXPost.Tests.Services;

public class ContactsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ContactsService _service;

    public ContactsServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DatabaseMigrations.ApplyContacts(_connection);
        var repo = new ContactRepository(_connection);
        _service = new ContactsService(repo);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void RecordContact_and_Autocomplete_returns_matches()
    {
        _service.RecordContact("alice@example.com", "Alice");
        var results = _service.Autocomplete("ali");

        Assert.Single(results);
        Assert.Equal("Alice <alice@example.com>", results[0]);
    }

    [Fact]
    public void Autocomplete_formats_without_name_when_null()
    {
        _service.RecordContact("noname@test.com", null);
        var results = _service.Autocomplete("noname");

        Assert.Single(results);
        Assert.Equal("noname@test.com", results[0]);
    }

    [Fact]
    public void Autocomplete_returns_most_used_first()
    {
        _service.RecordContact("rare@test.com", "Rare");
        _service.RecordContact("freq@test.com", "Frequent");
        _service.RecordContact("freq@test.com", "Frequent");
        _service.RecordContact("freq@test.com", "Frequent");

        var results = _service.Autocomplete("test.com");

        Assert.Equal(2, results.Count);
        Assert.StartsWith("Frequent", results[0]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd ~/source/cxpost && dotnet test
```

- [ ] **Step 3: Implement IContactsService and ContactsService**

Write `~/source/cxpost/CXPost/Services/IContactsService.cs`:

```csharp
namespace CXPost.Services;

public interface IContactsService
{
    void RecordContact(string address, string? displayName);
    List<string> Autocomplete(string query);
}
```

Write `~/source/cxpost/CXPost/Services/ContactsService.cs`:

```csharp
using CXPost.Data;

namespace CXPost.Services;

public class ContactsService : IContactsService
{
    private readonly ContactRepository _repo;

    public ContactsService(ContactRepository repo)
    {
        _repo = repo;
    }

    public void RecordContact(string address, string? displayName)
    {
        _repo.UpsertContact(address, displayName);
    }

    public List<string> Autocomplete(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var contacts = _repo.Search(query);
        return contacts.Select(c =>
            string.IsNullOrEmpty(c.DisplayName)
                ? c.Address
                : $"{c.DisplayName} <{c.Address}>")
            .ToList();
    }
}
```

- [ ] **Step 4: Run tests**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Services/IContactsService.cs CXPost/Services/ContactsService.cs CXPost.Tests/Services/ContactsServiceTests.cs
git commit -m "feat: add contacts service with autocomplete"
```

---

### Task 9: ColorScheme & DialogBase

**Files:**
- Create: `~/source/cxpost/CXPost/UI/Components/ColorScheme.cs`
- Create: `~/source/cxpost/CXPost/UI/Dialogs/DialogBase.cs`

- [ ] **Step 1: Create ColorScheme**

Write `~/source/cxpost/CXPost/UI/Components/ColorScheme.cs`:

```csharp
using SharpConsoleUI;

namespace CXPost.UI.Components;

public static class ColorScheme
{
    // Backgrounds
    public static readonly Color WindowBackground = Color.Grey11;
    public static readonly Color PanelBackground = Color.Grey15;
    public static readonly Color HeaderBackground = Color.Grey19;

    // Borders
    public static readonly Color BorderColor = Color.Grey23;
    public static readonly Color ActiveBorderColor = Color.SteelBlue;

    // Text
    public static readonly Color PrimaryText = Color.Cyan1;
    public static readonly Color SecondaryText = Color.Grey70;
    public static readonly Color MutedText = Color.Grey50;

    // Status
    public static readonly Color Success = Color.Green;
    public static readonly Color Warning = Color.Yellow;
    public static readonly Color Error = Color.Red;
    public static readonly Color Info = Color.Cyan1;

    // Mail-specific
    public static readonly Color Unread = Color.White;
    public static readonly Color Read = Color.Grey50;
    public static readonly Color Flagged = Color.Yellow;
    public static readonly Color SelectedRow = Color.SteelBlue;

    // Markup strings
    public const string PrimaryMarkup = "cyan1";
    public const string MutedMarkup = "grey50";
    public const string UnreadMarkup = "white bold";
    public const string ReadMarkup = "grey50";
    public const string FlaggedMarkup = "yellow";
    public const string ErrorMarkup = "red";
    public const string SuccessMarkup = "green";
}
```

- [ ] **Step 2: Create DialogBase**

Write `~/source/cxpost/CXPost/UI/Dialogs/DialogBase.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Events;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public abstract class DialogBase<TResult>
{
    private readonly TaskCompletionSource<TResult> _tcs = new();
    protected TResult? Result { get; set; }
    protected Window Modal { get; private set; } = null!;
    protected ConsoleWindowSystem WindowSystem { get; private set; } = null!;

    public Task<TResult> ShowAsync(ConsoleWindowSystem windowSystem)
    {
        WindowSystem = windowSystem;
        Modal = CreateModal();
        BuildContent();
        AttachEventHandlers();
        WindowSystem.AddWindow(Modal);
        WindowSystem.SetActiveWindow(Modal);
        SetInitialFocus();
        return _tcs.Task;
    }

    protected virtual Window CreateModal()
    {
        var (w, h) = GetSize();
        var builder = new WindowBuilder(WindowSystem)
            .AsModal()
            .WithTitle(GetTitle())
            .WithSize(w, h)
            .Centered()
            .Resizable(GetResizable())
            .Movable(true)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(ColorScheme.BorderColor);

        return builder.Build();
    }

    protected abstract void BuildContent();
    protected abstract string GetTitle();
    protected virtual (int width, int height) GetSize() => (60, 18);
    protected virtual bool GetResizable() => true;
    protected virtual void SetInitialFocus() { }
    protected virtual TResult GetDefaultResult() => default!;
    protected virtual void OnCleanup() { }

    protected void CloseWithResult(TResult result)
    {
        Result = result;
        Modal.Close();
    }

    protected virtual void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseWithResult(GetDefaultResult());
            e.Handled = true;
        }
    }

    private void AttachEventHandlers()
    {
        Modal.KeyPressed += OnKeyPressed;
        Modal.OnClosed += OnModalClosed;
    }

    private void OnModalClosed(object? sender, EventArgs e)
    {
        OnCleanup();
        _tcs.TrySetResult(Result ?? GetDefaultResult());
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
cd ~/source/cxpost
git add CXPost/UI/
git commit -m "feat: add ColorScheme and DialogBase foundation"
```

---

### Task 10: UI Shell — CXPostApp Main Window

**Files:**
- Create: `~/source/cxpost/CXPost/UI/CXPostApp.cs`
- Create: `~/source/cxpost/CXPost/UI/Components/StatusBarBuilder.cs`
- Create: `~/source/cxpost/CXPost/UI/KeyBindings.cs`
- Modify: `~/source/cxpost/CXPost/Program.cs`

- [ ] **Step 1: Create StatusBarBuilder**

Write `~/source/cxpost/CXPost/UI/Components/StatusBarBuilder.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Panel;

namespace CXPost.UI.Components;

public class StatusBarBuilder
{
    private readonly MarkupControl _topLeft;
    private readonly MarkupControl _topRight;
    private readonly MarkupControl _bottomLeft;

    public StatusBarBuilder()
    {
        _topLeft = Controls.Markup("[cyan1]CXPost[/]").Build();
        _topRight = Controls.Markup("[grey50]Disconnected[/]").Build();
        _bottomLeft = Controls.Markup("[grey50]Ctrl+N[/]: Compose  [grey50]Ctrl+R[/]: Reply  [grey50]Ctrl+S[/]: Search  [grey50]Del[/]: Delete  [grey50]Ctrl+M[/]: Move").Build();
    }

    public IPanelElement TopLeftElement => new ControlPanelElement(_topLeft);
    public IPanelElement TopRightElement => new ControlPanelElement(_topRight);
    public IPanelElement BottomLeftElement => new ControlPanelElement(_bottomLeft);

    public void UpdateBreadcrumb(string accountName, string folderName)
    {
        _topLeft.SetContent([$"[cyan1]CXPost[/] [grey50]|[/] [{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(accountName)}[/] [grey50]>[/] {MarkupParser.Escape(folderName)}"]);
    }

    public void UpdateConnectionStatus(int unreadCount, bool connected)
    {
        var status = connected
            ? $"[{ColorScheme.FlaggedMarkup}]{unreadCount} unread[/] [grey50]|[/] [{ColorScheme.SuccessMarkup}]● Connected[/]"
            : $"[{ColorScheme.ErrorMarkup}]● Offline[/]";
        _topRight.SetContent([status]);
    }

    public void UpdateHelpBar(string context)
    {
        _bottomLeft.SetContent([context]);
    }

    private class ControlPanelElement : IPanelElement
    {
        private readonly MarkupControl _control;
        public ControlPanelElement(MarkupControl control) => _control = control;
        public int MeasureWidth(int availableWidth) => availableWidth;
        public string Render(int width) => _control.GetContent();
    }
}
```

Note: The `ControlPanelElement` implementation may need adjustment based on the actual `IPanelElement` interface in ConsoleEx. The agent implementing this task should check the actual interface.

- [ ] **Step 2: Create KeyBindings**

Write `~/source/cxpost/CXPost/UI/KeyBindings.cs`:

```csharp
namespace CXPost.UI;

public static class KeyBindings
{
    // Compose
    public static readonly ConsoleKey ComposeNew = ConsoleKey.N;           // Ctrl+N
    public static readonly ConsoleKey Reply = ConsoleKey.R;                // Ctrl+R
    public static readonly ConsoleKey Forward = ConsoleKey.F;              // Ctrl+F
    public static readonly ConsoleKey Send = ConsoleKey.Enter;             // Ctrl+Enter

    // Navigation
    public static readonly ConsoleKey Search = ConsoleKey.S;               // Ctrl+S
    public static readonly ConsoleKey MoveToFolder = ConsoleKey.M;         // Ctrl+M
    public static readonly ConsoleKey Refresh = ConsoleKey.F5;             // F5
    public static readonly ConsoleKey SwitchLayout = ConsoleKey.F8;        // F8
    public static readonly ConsoleKey Settings = ConsoleKey.OemComma;      // Ctrl+,

    // Message actions
    public static readonly ConsoleKey Delete = ConsoleKey.Delete;          // Del
    public static readonly ConsoleKey ToggleFlag = ConsoleKey.D;           // Ctrl+D
    public static readonly ConsoleKey ToggleRead = ConsoleKey.U;           // Ctrl+U

    // Compose actions
    public static readonly ConsoleKey SaveDraft = ConsoleKey.S;            // Ctrl+S (in compose)
}
```

- [ ] **Step 3: Create CXPostApp**

Write `~/source/cxpost/CXPost/UI/CXPostApp.cs`:

```csharp
using System.Collections.Concurrent;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI;

public class CXPostApp : IDisposable
{
    private readonly ConsoleWindowSystem _ws;
    private readonly IConfigService _configService;
    private readonly ICacheService _cacheService;
    private readonly StatusBarBuilder _statusBar;
    private readonly ConcurrentQueue<Action> _pendingUiActions = new();
    private readonly CancellationTokenSource _cts = new();

    private Window? _mainWindow;
    private CXPostConfig _config;
    private string _currentLayout = "classic";

    // Panels (created during layout setup)
    private TreeControl? _folderTree;
    private TableControl? _messageTable;
    private ScrollablePanelControl? _readingPane;
    private MarkupControl? _readingContent;
    private HorizontalGridControl? _mainGrid;

    public CXPostApp(
        ConsoleWindowSystem ws,
        IConfigService configService,
        ICacheService cacheService)
    {
        _ws = ws;
        _configService = configService;
        _cacheService = cacheService;
        _statusBar = new StatusBarBuilder();
        _config = configService.Load();
        _currentLayout = _config.Layout;
    }

    public void Run()
    {
        CreateLayout();
        _ws.Run();
    }

    private void CreateLayout()
    {
        var desktop = _ws.DesktopDimensions;

        // Folder tree
        _folderTree = Controls.Tree()
            .WithGuide(TreeGuide.Line)
            .WithHighlightColors(Color.White, ColorScheme.SelectedRow)
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .WithForegroundColor(ColorScheme.SecondaryText)
            .Build();

        _folderTree.SelectedNodeChanged += OnFolderSelected;

        // Message table
        _messageTable = Controls.Table()
            .AddColumn("★", TextJustification.Center, width: 3)
            .AddColumn("From", width: 24)
            .AddColumn("Subject")
            .AddColumn("Date", TextJustification.Right, width: 12)
            .EnableSorting()
            .OnSelectedRowChanged(OnMessageSelected)
            .OnRowActivated(OnMessageActivated)
            .Build();

        _messageTable.HorizontalAlignment = HorizontalAlignment.Stretch;
        _messageTable.VerticalAlignment = VerticalAlignment.Fill;

        // Reading pane
        _readingContent = Controls.Markup("[grey50]Select a message to read[/]").Build();
        _readingContent.HorizontalAlignment = HorizontalAlignment.Stretch;

        _readingPane = Controls.ScrollablePanel()
            .AddControl(_readingContent)
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        // Splitter between message list and reading pane
        var listReadingSplitter = Controls.HorizontalSplitter()
            .WithMinHeights(5, 5)
            .Build();

        // Build layout
        _mainGrid = Controls.HorizontalGrid()
            .Column(col => col.Width(28).Add(_folderTree))
            .Column(col =>
            {
                col.Add(_messageTable);
                col.Add(listReadingSplitter);
                col.Add(_readingPane);
            })
            .WithSplitterAfter(0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        _mainWindow = new WindowBuilder(_ws)
            .HideTitle()
            .Borderless()
            .Maximized()
            .Movable(false)
            .Resizable(false)
            .Minimizable(false)
            .Maximizable(false)
            .AddControl(_mainGrid)
            .WithAsyncWindowThread(MainLoopAsync)
            .OnKeyPressed(OnKeyPressed)
            .Build();

        _ws.AddWindow(_mainWindow);
        _ws.SetActiveWindow(_mainWindow);

        // Populate folder tree with cached data
        PopulateFolderTree();
    }

    private async Task MainLoopAsync(Window window, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Drain UI update queue
                while (_pendingUiActions.TryDequeue(out var action))
                    action();

                await Task.Delay(80, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void PopulateFolderTree()
    {
        if (_folderTree == null) return;
        _folderTree.Clear();

        // "All Inboxes" virtual node
        var allInboxes = _folderTree.AddRootNode("All Inboxes");
        allInboxes.TextColor = ColorScheme.PrimaryText;

        foreach (var account in _config.Accounts)
        {
            var accountNode = _folderTree.AddRootNode(account.Name);
            accountNode.TextColor = ColorScheme.MutedText;
            accountNode.Tag = account;

            var folders = _cacheService.GetFolders(account.Id);
            foreach (var folder in folders.OrderBy(f => f.Path))
            {
                var text = folder.UnreadCount > 0
                    ? $"{folder.DisplayName} [yellow]({folder.UnreadCount})[/]"
                    : folder.DisplayName;

                var node = accountNode.AddChild(text);
                node.Tag = folder;
            }
        }
    }

    public void EnqueueUiAction(Action action)
    {
        _pendingUiActions.Enqueue(action);
    }

    private void OnFolderSelected(object? sender, TreeNodeEventArgs args)
    {
        if (args.Node?.Tag is MailFolder folder)
        {
            var messages = _cacheService.GetMessages(folder.Id);
            PopulateMessageList(messages);

            // Find account for breadcrumb
            var account = _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
            _statusBar.UpdateBreadcrumb(account?.Name ?? "Unknown", folder.DisplayName);
        }
    }

    private void PopulateMessageList(List<MailMessage> messages)
    {
        if (_messageTable == null) return;

        _messageTable.ClearRows();
        foreach (var msg in messages)
        {
            var star = msg.IsFlagged ? "[yellow]★[/]" : "☆";
            var from = msg.IsRead
                ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]"
                : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]";
            var subject = msg.IsRead
                ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]"
                : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]";
            var date = FormatDate(msg.Date);

            var row = new TableRow(star, from, subject, date);
            row.Tag = msg;
            _messageTable.AddRow(row);
        }
    }

    private void OnMessageSelected(object? sender, int rowIndex)
    {
        // Preview in reading pane (headers only if body not fetched)
        if (_messageTable == null || rowIndex < 0) return;
        var row = _messageTable.GetRow(rowIndex);
        if (row?.Tag is not MailMessage msg) return;

        ShowMessagePreview(msg);
    }

    private void OnMessageActivated(object? sender, int rowIndex)
    {
        // Full message view — trigger body fetch if needed
        OnMessageSelected(sender, rowIndex);
    }

    private void ShowMessagePreview(MailMessage msg)
    {
        if (_readingContent == null) return;

        var lines = new List<string>
        {
            $"[{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]",
            $"[{ColorScheme.MutedMarkup}]From:[/] {MarkupParser.Escape(msg.FromName ?? "")} <{MarkupParser.Escape(msg.FromAddress ?? "")}>",
            $"[{ColorScheme.MutedMarkup}]Date:[/] {msg.Date:MMMM d, yyyy h:mm tt}",
            $"[{ColorScheme.MutedMarkup}]To:[/] {MarkupParser.Escape(msg.ToAddresses ?? "")}",
            ""
        };

        if (msg.BodyFetched && msg.BodyPlain != null)
        {
            lines.Add("───────────────────────────────────────");
            lines.AddRange(msg.BodyPlain.Split('\n').Select(MarkupParser.Escape));
        }
        else
        {
            lines.Add($"[{ColorScheme.MutedMarkup}]Loading message body...[/]");
        }

        _readingContent.SetContent(lines);
    }

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift);

        if (ctrl && e.KeyInfo.Key == KeyBindings.ComposeNew)
        {
            // TODO: Task 13 — open ComposeDialog
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Reply)
        {
            // TODO: Task 12 — inline reply
            e.Handled = true;
        }
        else if (ctrl && shift && e.KeyInfo.Key == KeyBindings.Reply)
        {
            // TODO: Task 12 — reply all
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Forward)
        {
            // TODO: Task 12 — forward
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Search)
        {
            // TODO: Task 14 — search dialog
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Delete)
        {
            // TODO: Task 11 — delete message
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleFlag)
        {
            // TODO: Task 11 — toggle flag
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleRead)
        {
            // TODO: Task 11 — toggle read
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.MoveToFolder)
        {
            // TODO: Task 14 — move to folder
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Refresh)
        {
            // TODO: Task 11 — force sync
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.SwitchLayout)
        {
            // TODO: Toggle layout
            e.Handled = true;
        }
    }

    private static string FormatDate(DateTime date)
    {
        var now = DateTime.UtcNow;
        if (date.Date == now.Date)
            return date.ToString("h:mm tt");
        if (date.Year == now.Year)
            return date.ToString("MMM d");
        return date.ToString("MMM d, yyyy");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
```

- [ ] **Step 4: Update Program.cs with DI container**

Write `~/source/cxpost/CXPost/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using CXPost.Data;
using CXPost.Services;
using CXPost.UI;

namespace CXPost;

public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            if (width <= 0 || height <= 0)
            {
                Console.Error.WriteLine("CXPost requires an interactive terminal.");
                return 1;
            }
        }
        catch
        {
            Console.Error.WriteLine("CXPost requires an interactive terminal.");
            return 1;
        }

        // Resolve OS-standard paths
        var configDir = GetConfigDir();
        var dataDir = GetDataDir();
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(dataDir);

        // Open SQLite databases
        var mailDbPath = Path.Combine(dataDir, "mail.db");
        var contactsDbPath = Path.Combine(dataDir, "contacts.db");

        var mailConn = new SqliteConnection($"Data Source={mailDbPath}");
        mailConn.Open();
        DatabaseMigrations.Apply(mailConn);

        var contactsConn = new SqliteConnection($"Data Source={contactsDbPath}");
        contactsConn.Open();
        DatabaseMigrations.ApplyContacts(contactsConn);

        try
        {
            // DI container
            var services = new ServiceCollection();

            // Window system
            var ws = new ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer));
            services.AddSingleton(ws);

            // Repositories
            services.AddSingleton(new MailRepository(mailConn));
            services.AddSingleton(new ContactRepository(contactsConn));

            // Services
            services.AddSingleton<IConfigService>(new ConfigService(configDir));
            services.AddSingleton<ICredentialService, CredentialService>();
            services.AddSingleton<ICacheService, CacheService>();
            services.AddSingleton<IContactsService, ContactsService>();
            services.AddSingleton<IImapService, ImapService>();
            services.AddSingleton<ISmtpService, SmtpService>();
            services.AddSingleton<ThreadingService>();

            // App
            services.AddSingleton<CXPostApp>();

            var provider = services.BuildServiceProvider();

            using var app = provider.GetRequiredService<CXPostApp>();
            app.Run();
        }
        finally
        {
            mailConn.Dispose();
            contactsConn.Dispose();
        }

        return 0;
    }

    private static string GetConfigDir()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CXPost");
    }

    private static string GetDataDir()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CXPost");
    }
}
```

- [ ] **Step 5: Create directory structure**

```bash
mkdir -p ~/source/cxpost/CXPost/UI/Panels
mkdir -p ~/source/cxpost/CXPost/UI/Dialogs
mkdir -p ~/source/cxpost/CXPost/UI/Components
mkdir -p ~/source/cxpost/CXPost/Coordinators
```

- [ ] **Step 6: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

Expected: Build succeeded. Some methods may reference types that need adjusting based on actual ConsoleEx API (e.g., `MarkupParser.Escape`, `TreeNodeEventArgs`, `TableRow`). The implementing agent should verify against ConsoleEx source.

- [ ] **Step 7: Commit**

```bash
cd ~/source/cxpost
git add CXPost/UI/ CXPost/Program.cs
git commit -m "feat: add CXPostApp shell with 3-pane layout and DI bootstrap"
```

---

### Task 11: MailSync Coordinator & Message Actions

**Files:**
- Create: `~/source/cxpost/CXPost/Coordinators/MailSyncCoordinator.cs`
- Create: `~/source/cxpost/CXPost/Coordinators/FolderCoordinator.cs`
- Create: `~/source/cxpost/CXPost/Coordinators/MessageListCoordinator.cs`
- Create: `~/source/cxpost/CXPost/Coordinators/NotificationCoordinator.cs`

- [ ] **Step 1: Create MailSyncCoordinator**

Write `~/source/cxpost/CXPost/Coordinators/MailSyncCoordinator.cs`:

```csharp
using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class MailSyncCoordinator
{
    private readonly IImapService _imap;
    private readonly ICacheService _cache;
    private readonly ThreadingService _threading;
    private readonly CXPostApp _app;
    private readonly ConcurrentDictionary<string, bool> _syncingAccounts = new();

    public MailSyncCoordinator(
        IImapService imap,
        ICacheService cache,
        ThreadingService threading,
        CXPostApp app)
    {
        _imap = imap;
        _cache = cache;
        _threading = threading;
        _app = app;
    }

    public async Task SyncAccountAsync(Account account, CancellationToken ct)
    {
        if (!_syncingAccounts.TryAdd(account.Id, true))
            return; // Already syncing

        try
        {
            if (!_imap.IsConnected)
                await _imap.ConnectAsync(account, ct);

            // Sync folder list
            var folders = await _imap.GetFoldersAsync(ct);
            _cache.SyncFolders(account.Id, folders);

            // Sync headers for each folder
            var cachedFolders = _cache.GetFolders(account.Id);
            foreach (var folder in cachedFolders)
            {
                await SyncFolderAsync(account, folder, ct);
            }

            _app.EnqueueUiAction(() => _app.RefreshFolderTree());
        }
        catch (Exception ex)
        {
            _app.EnqueueUiAction(() => _app.ShowError($"Sync failed for {account.Name}: {ex.Message}"));
        }
        finally
        {
            _syncingAccounts.TryRemove(account.Id, out _);
        }
    }

    public async Task SyncFolderAsync(Account account, MailFolder folder, CancellationToken ct)
    {
        // Check UIDVALIDITY
        var serverValidity = await _imap.GetUidValidityAsync(folder.Path, ct);
        if (folder.UidValidity != 0 && folder.UidValidity != serverValidity)
        {
            // UIDVALIDITY changed — purge and resync
            _cache.PurgeFolder(folder.Id);
        }

        // Delta sync: find new UIDs
        var serverUids = await _imap.GetUidsAsync(folder.Path, ct);
        var localUids = _cache.GetCachedUids(folder.Id);

        var newUids = serverUids.Except(localUids).ToHashSet();
        var removedUids = localUids.Except(serverUids).ToHashSet();

        // Remove deleted messages
        foreach (var uid in removedUids)
            _cache.DeleteMessage(folder.Id, uid);

        // Fetch new headers
        if (newUids.Count > 0)
        {
            var minUid = newUids.Min();
            var headers = await _imap.FetchHeadersAsync(folder.Path, minUid, ct);
            var newHeaders = headers.Where(h => newUids.Contains(h.Uid)).ToList();

            // Assign thread IDs
            var allMessages = _cache.GetMessages(folder.Id);
            allMessages.AddRange(newHeaders);
            _threading.AssignThreadIds(allMessages);

            _cache.SyncHeaders(folder.Id, newHeaders);
        }
    }

    public async Task FetchBodyAsync(MailFolder folder, MailMessage message, CancellationToken ct)
    {
        if (message.BodyFetched) return;

        var body = await _imap.FetchBodyAsync(folder.Path, message.Uid, ct);
        if (body != null)
        {
            _cache.StoreBody(folder.Id, message.Uid, body);
            message.BodyPlain = body;
            message.BodyFetched = true;
        }
    }

    public async Task StartIdleAsync(Account account, string folderPath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_imap.IsConnected)
                    await _imap.ConnectAsync(account, ct);

                await _imap.IdleAsync(folderPath, () =>
                {
                    // New message detected — queue sync
                    var folder = _cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == folderPath);
                    if (folder != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await SyncFolderAsync(account, folder, ct); }
                            catch { /* logged elsewhere */ }
                        }, ct);
                    }
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Reconnect with backoff
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
```

Note: This coordinator references `_app.RefreshFolderTree()` and `_app.ShowError()` — these methods need to be added to `CXPostApp`. The implementing agent should add:

```csharp
// Add to CXPostApp.cs
public void RefreshFolderTree() => PopulateFolderTree();

public void ShowError(string message)
{
    _ws.NotificationStateService.ShowNotification(
        "Error", message, NotificationSeverity.Danger, timeout: 5000);
}
```

- [ ] **Step 2: Create MessageListCoordinator**

Write `~/source/cxpost/CXPost/Coordinators/MessageListCoordinator.cs`:

```csharp
using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class MessageListCoordinator
{
    private readonly ICacheService _cache;
    private readonly IImapService _imap;
    private readonly MailSyncCoordinator _sync;
    private readonly CXPostApp _app;

    public MailFolder? CurrentFolder { get; private set; }
    public MailMessage? SelectedMessage { get; private set; }

    public MessageListCoordinator(
        ICacheService cache,
        IImapService imap,
        MailSyncCoordinator sync,
        CXPostApp app)
    {
        _cache = cache;
        _imap = imap;
        _sync = sync;
        _app = app;
    }

    public void SelectFolder(MailFolder folder)
    {
        CurrentFolder = folder;
        RefreshMessageList();
    }

    public void SelectMessage(MailMessage message)
    {
        SelectedMessage = message;
    }

    public void RefreshMessageList()
    {
        if (CurrentFolder == null) return;
        var messages = _cache.GetMessages(CurrentFolder.Id);
        _app.EnqueueUiAction(() => _app.PopulateMessageList(messages));
    }

    public async Task ToggleFlagAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;

        var newFlag = !SelectedMessage.IsFlagged;
        await _imap.SetFlagsAsync(CurrentFolder.Path, SelectedMessage.Uid, isFlagged: newFlag, ct: ct);
        _cache.UpdateFlags(CurrentFolder.Id, SelectedMessage.Uid, SelectedMessage.IsRead, newFlag);
        SelectedMessage.IsFlagged = newFlag;
        RefreshMessageList();
    }

    public async Task ToggleReadAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;

        var newRead = !SelectedMessage.IsRead;
        await _imap.SetFlagsAsync(CurrentFolder.Path, SelectedMessage.Uid, isRead: newRead, ct: ct);
        _cache.UpdateFlags(CurrentFolder.Id, SelectedMessage.Uid, newRead, SelectedMessage.IsFlagged);
        SelectedMessage.IsRead = newRead;
        RefreshMessageList();
    }

    public async Task DeleteMessageAsync(CancellationToken ct)
    {
        if (CurrentFolder == null || SelectedMessage == null) return;

        // Move to Trash (or delete if already in Trash)
        var folders = _cache.GetFolders(CurrentFolder.AccountId);
        var trash = folders.FirstOrDefault(f =>
            f.Path.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("[Gmail]/Trash", StringComparison.OrdinalIgnoreCase));

        if (trash != null && CurrentFolder.Id != trash.Id)
        {
            await _imap.MoveMessageAsync(CurrentFolder.Path, trash.Path, SelectedMessage.Uid, ct);
        }
        else
        {
            await _imap.DeleteMessageAsync(CurrentFolder.Path, SelectedMessage.Uid, ct);
        }

        _cache.DeleteMessage(CurrentFolder.Id, SelectedMessage.Uid);
        SelectedMessage = null;
        RefreshMessageList();
    }

    public async Task FetchAndShowBodyAsync(MailMessage message, CancellationToken ct)
    {
        if (CurrentFolder == null) return;

        await _sync.FetchBodyAsync(CurrentFolder, message, ct);
        _app.EnqueueUiAction(() => _app.ShowMessagePreview(message));

        // Mark as read
        if (!message.IsRead)
        {
            await _imap.SetFlagsAsync(CurrentFolder.Path, message.Uid, isRead: true, ct: ct);
            _cache.UpdateFlags(CurrentFolder.Id, message.Uid, true, message.IsFlagged);
            message.IsRead = true;
            RefreshMessageList();
        }
    }
}
```

Note: This references `_app.PopulateMessageList()` and `_app.ShowMessagePreview()` which need to be made public in CXPostApp. The implementing agent should adjust access modifiers.

- [ ] **Step 3: Create NotificationCoordinator**

Write `~/source/cxpost/CXPost/Coordinators/NotificationCoordinator.cs`:

```csharp
using SharpConsoleUI;

namespace CXPost.Coordinators;

public class NotificationCoordinator
{
    private readonly ConsoleWindowSystem _ws;

    public NotificationCoordinator(ConsoleWindowSystem ws)
    {
        _ws = ws;
    }

    public void NotifyNewMail(string from, string subject)
    {
        _ws.NotificationStateService.ShowNotification(
            "New Mail",
            $"From: {from}\n{subject}",
            NotificationSeverity.Info,
            timeout: 5000);
    }

    public void NotifySendSuccess(string to)
    {
        _ws.NotificationStateService.ShowNotification(
            "Sent",
            $"Message sent to {to}",
            NotificationSeverity.Success,
            timeout: 3000);
    }

    public void NotifyError(string title, string message)
    {
        _ws.NotificationStateService.ShowNotification(
            title, message, NotificationSeverity.Danger, timeout: 5000);
    }
}
```

- [ ] **Step 4: Register coordinators in DI (update Program.cs)**

Add to the DI section in `Program.cs`, before `services.AddSingleton<CXPostApp>()`:

```csharp
services.AddSingleton<MailSyncCoordinator>();
services.AddSingleton<MessageListCoordinator>();
services.AddSingleton<NotificationCoordinator>();
```

- [ ] **Step 5: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

Expected: Build may have some issues with circular references (`MailSyncCoordinator` → `CXPostApp` → coordinators). The implementing agent should resolve by either making `CXPostApp` methods virtual/interface-based, or by using `Lazy<T>` in DI. A simple fix is to inject `CXPostApp` via `Lazy<CXPostApp>` in coordinators.

- [ ] **Step 6: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Coordinators/ CXPost/Program.cs CXPost/UI/CXPostApp.cs
git commit -m "feat: add sync, message list, and notification coordinators"
```

---

### Task 12: Compose Coordinator & Inline Reply

**Files:**
- Create: `~/source/cxpost/CXPost/Coordinators/ComposeCoordinator.cs`
- Create: `~/source/cxpost/CXPost/UI/Components/MessageFormatter.cs`

- [ ] **Step 1: Create MessageFormatter**

Write `~/source/cxpost/CXPost/UI/Components/MessageFormatter.cs`:

```csharp
using CXPost.Models;

namespace CXPost.UI.Components;

public static class MessageFormatter
{
    public static string FormatQuotedReply(MailMessage original)
    {
        var lines = new List<string>
        {
            "",
            "",
            $"On {original.Date:MMMM d, yyyy} at {original.Date:h:mm tt}, {original.FromName ?? original.FromAddress} wrote:",
            ""
        };

        if (original.BodyPlain != null)
        {
            foreach (var line in original.BodyPlain.Split('\n'))
                lines.Add($"> {line.TrimEnd('\r')}");
        }

        return string.Join('\n', lines);
    }

    public static string FormatForwardBody(MailMessage original)
    {
        var lines = new List<string>
        {
            "",
            "",
            "---------- Forwarded message ----------",
            $"From: {original.FromName ?? ""} <{original.FromAddress}>",
            $"Date: {original.Date:MMMM d, yyyy h:mm tt}",
            $"Subject: {original.Subject}",
            $"To: {original.ToAddresses}",
            ""
        };

        if (original.BodyPlain != null)
            lines.Add(original.BodyPlain);

        return string.Join('\n', lines);
    }

    public static string GetReplySubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return "Re: ";
        if (subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)) return subject;
        return $"Re: {subject}";
    }

    public static string GetForwardSubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return "Fwd: ";
        return $"Fwd: {subject}";
    }
}
```

- [ ] **Step 2: Create ComposeCoordinator**

Write `~/source/cxpost/CXPost/Coordinators/ComposeCoordinator.cs`:

```csharp
using CXPost.Models;
using CXPost.Services;
using CXPost.UI;
using CXPost.UI.Components;
using MimeKit;

namespace CXPost.Coordinators;

public class ComposeCoordinator
{
    private readonly ISmtpService _smtp;
    private readonly IImapService _imap;
    private readonly ICacheService _cache;
    private readonly IContactsService _contacts;
    private readonly IConfigService _configService;
    private readonly NotificationCoordinator _notifications;

    public ComposeCoordinator(
        ISmtpService smtp,
        IImapService imap,
        ICacheService cache,
        IContactsService contacts,
        IConfigService configService,
        NotificationCoordinator notifications)
    {
        _smtp = smtp;
        _imap = imap;
        _cache = cache;
        _contacts = contacts;
        _configService = configService;
        _notifications = notifications;
    }

    public async Task SendAsync(Account account, string to, string? cc, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.Name, account.Email));

        foreach (var addr in ParseAddresses(to))
            message.To.Add(addr);

        if (!string.IsNullOrWhiteSpace(cc))
        {
            foreach (var addr in ParseAddresses(cc))
                message.Cc.Add(addr);
        }

        message.Subject = subject;

        // Add signature
        var bodyWithSig = body;
        if (!string.IsNullOrEmpty(account.Signature))
            bodyWithSig = body + "\n\n" + account.Signature;

        message.Body = new TextPart("plain") { Text = bodyWithSig };

        // Send via SMTP
        if (!_smtp.IsConnected)
            await _smtp.ConnectAsync(account, ct);
        await _smtp.SendAsync(message, ct);

        // Copy to Sent folder
        var folders = _cache.GetFolders(account.Id);
        var sent = folders.FirstOrDefault(f =>
            f.Path.Equals("Sent", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("[Gmail]/Sent", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("Sent Messages", StringComparison.OrdinalIgnoreCase));

        if (sent != null)
            await _imap.AppendMessageAsync(sent.Path, message, MailKit.MessageFlags.Seen, ct);

        // Record contacts
        foreach (var addr in message.To.Mailboxes)
            _contacts.RecordContact(addr.Address, addr.Name);
        foreach (var addr in message.Cc.Mailboxes)
            _contacts.RecordContact(addr.Address, addr.Name);

        _notifications.NotifySendSuccess(to);
    }

    public (string to, string subject, string body) PrepareReply(Account account, MailMessage original, bool replyAll)
    {
        var to = original.FromAddress ?? "";
        if (replyAll && !string.IsNullOrEmpty(original.ToAddresses))
        {
            // Include all original recipients except self
            var allAddrs = System.Text.Json.JsonSerializer.Deserialize<List<string>>(original.ToAddresses) ?? [];
            var others = allAddrs.Where(a => !a.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
            var combined = new[] { to }.Concat(others).Distinct();
            to = string.Join(", ", combined);
        }

        var subject = MessageFormatter.GetReplySubject(original.Subject);
        var body = MessageFormatter.FormatQuotedReply(original);

        return (to, subject, body);
    }

    public (string to, string subject, string body) PrepareForward(MailMessage original)
    {
        var subject = MessageFormatter.GetForwardSubject(original.Subject);
        var body = MessageFormatter.FormatForwardBody(original);
        return ("", subject, body);
    }

    private static IEnumerable<MailboxAddress> ParseAddresses(string addresses)
    {
        foreach (var addr in addresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MailboxAddress.TryParse(addr, out var mailbox))
                yield return mailbox;
            else
                yield return new MailboxAddress(null, addr.Trim());
        }
    }
}
```

- [ ] **Step 3: Register ComposeCoordinator in DI (update Program.cs)**

Add to DI section:

```csharp
services.AddSingleton<ComposeCoordinator>();
```

- [ ] **Step 4: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

- [ ] **Step 5: Commit**

```bash
cd ~/source/cxpost
git add CXPost/Coordinators/ComposeCoordinator.cs CXPost/UI/Components/MessageFormatter.cs CXPost/Program.cs
git commit -m "feat: add compose coordinator with reply/forward support"
```

---

### Task 13: Compose Dialog (Modal for New Messages)

**Files:**
- Create: `~/source/cxpost/CXPost/UI/Dialogs/ComposeDialog.cs`

- [ ] **Step 1: Create ComposeDialog**

Write `~/source/cxpost/CXPost/UI/Dialogs/ComposeDialog.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public record ComposeResult(string To, string? Cc, string Subject, string Body);

public class ComposeDialog : DialogBase<ComposeResult?>
{
    private readonly IContactsService _contacts;
    private readonly string _initialTo;
    private readonly string _initialSubject;
    private readonly string _initialBody;

    private PromptControl? _toField;
    private PromptControl? _ccField;
    private PromptControl? _subjectField;
    private MultilineEditControl? _bodyEditor;

    public ComposeDialog(
        IContactsService contacts,
        string to = "",
        string subject = "",
        string body = "")
    {
        _contacts = contacts;
        _initialTo = to;
        _initialSubject = subject;
        _initialBody = body;
    }

    protected override string GetTitle() => "New Message";
    protected override (int width, int height) GetSize() => (80, 28);
    protected override bool GetResizable() => true;
    protected override ComposeResult? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // To field
        var toLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]To:[/]").Build();
        toLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(toLabel);

        _toField = new PromptControl { Prompt = "", Value = _initialTo };
        _toField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_toField);

        // Cc field
        var ccLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Cc:[/]").Build();
        ccLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(ccLabel);

        _ccField = new PromptControl { Prompt = "", Value = "" };
        _ccField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_ccField);

        // Subject field
        var subjectLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Subject:[/]").Build();
        subjectLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(subjectLabel);

        _subjectField = new PromptControl { Prompt = "", Value = _initialSubject };
        _subjectField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_subjectField);

        // Separator
        var rule = Controls.Markup($"[{ColorScheme.MutedMarkup}]{"─".PadRight(76, '─')}[/]").Build();
        Modal.AddControl(rule);

        // Body editor
        _bodyEditor = Controls.MultilineEdit(_initialBody)
            .WithWrapMode(WrapMode.Word)
            .WithPlaceholder("Type your message here...")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .IsEditing()
            .Build();

        Modal.AddControl(_bodyEditor);

        // Help text
        var helpText = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]Ctrl+Enter: Send  |  Ctrl+S: Save Draft  |  Esc: Discard[/]")
            .Build();
        helpText.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(helpText);
    }

    protected override void SetInitialFocus()
    {
        if (string.IsNullOrEmpty(_initialTo))
            _toField?.Focus();
        else
            _bodyEditor?.Focus();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);

        if (ctrl && e.KeyInfo.Key == ConsoleKey.Enter)
        {
            // Send
            var to = _toField?.Value ?? "";
            if (string.IsNullOrWhiteSpace(to))
                return; // Don't send without recipient

            CloseWithResult(new ComposeResult(
                To: to,
                Cc: string.IsNullOrWhiteSpace(_ccField?.Value) ? null : _ccField.Value,
                Subject: _subjectField?.Value ?? "",
                Body: _bodyEditor?.Text ?? ""));

            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

Note: Some ConsoleEx API usage (e.g., `PromptControl.Value`, `MultilineEditControl.Text`, `.Focus()`) may need adjustment based on actual API. The implementing agent should verify.

- [ ] **Step 3: Commit**

```bash
cd ~/source/cxpost
git add CXPost/UI/Dialogs/ComposeDialog.cs
git commit -m "feat: add compose dialog for new messages"
```

---

### Task 14: Search & Folder Picker Dialogs

**Files:**
- Create: `~/source/cxpost/CXPost/UI/Dialogs/SearchDialog.cs`
- Create: `~/source/cxpost/CXPost/UI/Dialogs/FolderPickerDialog.cs`
- Create: `~/source/cxpost/CXPost/Coordinators/SearchCoordinator.cs`

- [ ] **Step 1: Create SearchDialog**

Write `~/source/cxpost/CXPost/UI/Dialogs/SearchDialog.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class SearchDialog : DialogBase<string?>
{
    private PromptControl? _searchField;

    protected override string GetTitle() => "Search Messages";
    protected override (int width, int height) GetSize() => (60, 8);
    protected override bool GetResizable() => false;
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var label = Controls.Markup($"[{ColorScheme.PrimaryMarkup}]Search by subject, sender, or body:[/]").Build();
        label.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(label);

        _searchField = new PromptControl { Prompt = "🔍 " };
        _searchField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_searchField);

        var help = Controls.Markup($"[{ColorScheme.MutedMarkup}]Enter: Search  |  Esc: Cancel[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _searchField?.Focus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var query = _searchField?.Value?.Trim();
            if (!string.IsNullOrEmpty(query))
                CloseWithResult(query);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
```

- [ ] **Step 2: Create FolderPickerDialog**

Write `~/source/cxpost/CXPost/UI/Dialogs/FolderPickerDialog.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class FolderPickerDialog : DialogBase<MailFolder?>
{
    private readonly List<MailFolder> _folders;
    private ListControl? _folderList;

    public FolderPickerDialog(List<MailFolder> folders)
    {
        _folders = folders;
    }

    protected override string GetTitle() => "Move to Folder";
    protected override (int width, int height) GetSize() => (40, 16);
    protected override bool GetResizable() => false;
    protected override MailFolder? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        _folderList = Controls.List("Select folder:")
            .WithAutoHighlightOnFocus(true)
            .Build();

        foreach (var folder in _folders)
            _folderList.AddItem(folder.DisplayName);

        _folderList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _folderList.VerticalAlignment = VerticalAlignment.Fill;
        Modal.AddControl(_folderList);

        var help = Controls.Markup($"[{ColorScheme.MutedMarkup}]Enter: Select  |  Esc: Cancel[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _folderList?.Focus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var idx = _folderList?.SelectedIndex ?? -1;
            if (idx >= 0 && idx < _folders.Count)
                CloseWithResult(_folders[idx]);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
```

- [ ] **Step 3: Create SearchCoordinator**

Write `~/source/cxpost/CXPost/Coordinators/SearchCoordinator.cs`:

```csharp
using CXPost.Models;
using CXPost.Services;
using CXPost.UI;

namespace CXPost.Coordinators;

public class SearchCoordinator
{
    private readonly IImapService _imap;
    private readonly ICacheService _cache;

    public SearchCoordinator(IImapService imap, ICacheService cache)
    {
        _imap = imap;
        _cache = cache;
    }

    public async Task<List<MailMessage>> SearchAsync(MailFolder folder, string query, CancellationToken ct)
    {
        var matchingUids = await _imap.SearchAsync(folder.Path, query, ct);
        var allMessages = _cache.GetMessages(folder.Id);
        return allMessages.Where(m => matchingUids.Contains(m.Uid)).ToList();
    }
}
```

- [ ] **Step 4: Register SearchCoordinator in DI**

Add to DI section in `Program.cs`:

```csharp
services.AddSingleton<SearchCoordinator>();
```

- [ ] **Step 5: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

- [ ] **Step 6: Commit**

```bash
cd ~/source/cxpost
git add CXPost/UI/Dialogs/ CXPost/Coordinators/SearchCoordinator.cs CXPost/Program.cs
git commit -m "feat: add search dialog, folder picker, and search coordinator"
```

---

### Task 15: Settings & Account Setup Dialogs

**Files:**
- Create: `~/source/cxpost/CXPost/UI/Dialogs/SettingsDialog.cs`
- Create: `~/source/cxpost/CXPost/UI/Dialogs/AccountSetupDialog.cs`

- [ ] **Step 1: Create AccountSetupDialog**

Write `~/source/cxpost/CXPost/UI/Dialogs/AccountSetupDialog.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class AccountSetupDialog : DialogBase<Account?>
{
    private readonly Account? _existing;
    private PromptControl? _nameField;
    private PromptControl? _emailField;
    private PromptControl? _imapHostField;
    private PromptControl? _imapPortField;
    private PromptControl? _smtpHostField;
    private PromptControl? _smtpPortField;
    private PromptControl? _passwordField;

    public AccountSetupDialog(Account? existing = null)
    {
        _existing = existing;
    }

    protected override string GetTitle() => _existing != null ? "Edit Account" : "Add Account";
    protected override (int width, int height) GetSize() => (60, 24);
    protected override Account? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        void AddField(string label, out PromptControl field, string value = "")
        {
            Modal.AddControl(Controls.Markup($"[{ColorScheme.MutedMarkup}]{label}:[/]").Build());
            field = new PromptControl { Prompt = "", Value = value };
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            Modal.AddControl(field);
        }

        AddField("Display Name", out _nameField!, _existing?.Name ?? "");
        AddField("Email Address", out _emailField!, _existing?.Email ?? "");
        AddField("IMAP Host", out _imapHostField!, _existing?.ImapHost ?? "");
        AddField("IMAP Port", out _imapPortField!, _existing?.ImapPort.ToString() ?? "993");
        AddField("SMTP Host", out _smtpHostField!, _existing?.SmtpHost ?? "");
        AddField("SMTP Port", out _smtpPortField!, _existing?.SmtpPort.ToString() ?? "587");
        AddField("Password", out _passwordField!, "");

        var help = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]Ctrl+Enter: Save  |  Esc: Cancel[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _nameField?.Focus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var account = _existing ?? new Account();
            account.Name = _nameField?.Value ?? "";
            account.Email = _emailField?.Value ?? "";
            account.ImapHost = _imapHostField?.Value ?? "";
            account.ImapPort = int.TryParse(_imapPortField?.Value, out var ip) ? ip : 993;
            account.SmtpHost = _smtpHostField?.Value ?? "";
            account.SmtpPort = int.TryParse(_smtpPortField?.Value, out var sp) ? sp : 587;
            account.Username = account.Email;

            // Password stored separately via CredentialService
            // The caller handles this
            CloseWithResult(account);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    public string? GetPassword() => _passwordField?.Value;
}
```

- [ ] **Step 2: Create SettingsDialog**

Write `~/source/cxpost/CXPost/UI/Dialogs/SettingsDialog.cs`:

```csharp
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class SettingsDialog : DialogBase<bool>
{
    private readonly CXPostConfig _config;
    private ListControl? _accountList;

    public SettingsDialog(CXPostConfig config)
    {
        _config = config;
    }

    protected override string GetTitle() => "Settings";
    protected override (int width, int height) GetSize() => (50, 18);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var header = Controls.Markup($"[{ColorScheme.PrimaryMarkup}]Accounts[/]").Build();
        Modal.AddControl(header);

        _accountList = Controls.List()
            .WithAutoHighlightOnFocus(true)
            .Build();

        foreach (var account in _config.Accounts)
            _accountList.AddItem($"{account.Name} ({account.Email})");

        _accountList.AddItem("[+ Add Account]");
        _accountList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _accountList.VerticalAlignment = VerticalAlignment.Fill;
        Modal.AddControl(_accountList);

        var help = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]Enter: Edit  |  Esc: Close[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _accountList?.Focus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            // The caller handles opening AccountSetupDialog based on selection
            CloseWithResult(true);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

- [ ] **Step 4: Commit**

```bash
cd ~/source/cxpost
git add CXPost/UI/Dialogs/
git commit -m "feat: add settings and account setup dialogs"
```

---

### Task 16: Wire Key Bindings to Coordinators

**Files:**
- Modify: `~/source/cxpost/CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Update CXPostApp to inject coordinators and wire key handlers**

Update the `CXPostApp` constructor to accept coordinators:

```csharp
public CXPostApp(
    ConsoleWindowSystem ws,
    IConfigService configService,
    ICacheService cacheService,
    IContactsService contactsService,
    MailSyncCoordinator syncCoordinator,
    MessageListCoordinator messageListCoordinator,
    ComposeCoordinator composeCoordinator,
    SearchCoordinator searchCoordinator,
    NotificationCoordinator notificationCoordinator)
```

Store as fields. Then update `OnKeyPressed` to call actual coordinator methods instead of TODO comments. For example:

```csharp
if (ctrl && e.KeyInfo.Key == KeyBindings.ComposeNew)
{
    _ = Task.Run(async () =>
    {
        var dialog = new ComposeDialog(_contactsService);
        var result = await dialog.ShowAsync(_ws);
        if (result != null)
        {
            var account = _config.Accounts.FirstOrDefault();
            if (account != null)
                await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, _cts.Token);
        }
    });
    e.Handled = true;
}
```

The implementing agent should wire all the key handlers to their corresponding coordinators following this pattern.

- [ ] **Step 2: Add public methods needed by coordinators**

Add to `CXPostApp.cs`:

```csharp
// Methods called by coordinators via EnqueueUiAction
public void RefreshFolderTree() => PopulateFolderTree();
public new void PopulateMessageList(List<MailMessage> messages) => base.PopulateMessageList(messages);
public new void ShowMessagePreview(MailMessage msg) => base.ShowMessagePreview(msg);

public void ShowError(string message)
{
    _ws.NotificationStateService.ShowNotification(
        "Error", message, NotificationSeverity.Danger, timeout: 5000);
}
```

- [ ] **Step 3: Start sync on app launch**

Add to the end of `CreateLayout()`:

```csharp
// Start background sync for all accounts
foreach (var account in _config.Accounts)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
            await _syncCoordinator.StartIdleAsync(account, "INBOX", _cts.Token);
        }
        catch (Exception ex)
        {
            EnqueueUiAction(() => ShowError($"Sync error: {ex.Message}"));
        }
    }, _cts.Token);
}
```

- [ ] **Step 4: Build to verify**

```bash
cd ~/source/cxpost && dotnet build
```

- [ ] **Step 5: Commit**

```bash
cd ~/source/cxpost
git add CXPost/UI/CXPostApp.cs CXPost/Program.cs
git commit -m "feat: wire key bindings to coordinators, start background sync"
```

---

### Task 17: Integration Smoke Test & First Run

**Files:**
- Modify: `~/source/cxpost/CXPost/Program.cs` (first-run experience)

- [ ] **Step 1: Add first-run account setup flow**

When no accounts are configured, CXPost should prompt the user to add one. Add to `Program.cs` after building the service provider, before `app.Run()`:

```csharp
var config = provider.GetRequiredService<IConfigService>().Load();
if (config.Accounts.Count == 0)
{
    Console.Clear();
    Console.WriteLine("Welcome to CXPost!");
    Console.WriteLine("No email accounts configured. Opening setup...");
    Console.WriteLine("Press any key to continue.");
    Console.ReadKey(true);
}
```

The full first-run wizard (showing AccountSetupDialog on launch when no accounts exist) will be handled inside `CXPostApp.CreateLayout()` — after the window system starts, check if accounts are empty and auto-open the settings dialog.

- [ ] **Step 2: Add auto-open settings on first run in CXPostApp**

At the end of `CreateLayout()`, before starting sync:

```csharp
if (_config.Accounts.Count == 0)
{
    _ = Task.Run(async () =>
    {
        var setupDialog = new AccountSetupDialog();
        var account = await setupDialog.ShowAsync(_ws);
        if (account != null)
        {
            _config.Accounts.Add(account);
            _configService.Save(_config);

            // Store password if provided
            var password = setupDialog.GetPassword();
            if (!string.IsNullOrEmpty(password))
                _credentialService.StorePassword(account.Id, password);

            EnqueueUiAction(() => RefreshFolderTree());

            // Start sync
            await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
        }
    });
}
```

- [ ] **Step 3: Full build and manual test**

```bash
cd ~/source/cxpost && dotnet build && dotnet run --project CXPost
```

Expected: App launches, shows empty 3-pane layout (no accounts) or opens account setup dialog. Verify the UI renders correctly and Escape exits cleanly.

- [ ] **Step 4: Run all tests**

```bash
cd ~/source/cxpost && dotnet test
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
cd ~/source/cxpost
git add -A
git commit -m "feat: add first-run experience and integration wiring"
```
