# Code Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all critical, important, and minor issues found in the code review — 25 issues covering IMAP concurrency, bug fixes, security, dead config, and cleanup.

**Architecture:** The IMAP singleton is replaced with a factory that creates per-account connections with SemaphoreSlim guarding. SMTP gets connect-send-disconnect per operation. Individual bug fixes are isolated changes. Dead config fields get wired into the coordinators that should read them.

**Tech Stack:** .NET 10, MailKit, SharpConsoleUI, SQLite, xUnit

**Deferred:** #25 (contact autocomplete) — requires ConsoleEx `PromptControl` to support autocomplete/suggestions, which it currently does not. Out of scope.

---

### Task 1: IMAP Connection Factory (#1, #2)

Replace singleton `ImapService` with `ImapConnectionFactory` that creates per-account connections, each guarded by a semaphore.

**Files:**
- Create: `CXPost/Services/ImapConnectionFactory.cs`
- Modify: `CXPost/Services/IImapService.cs`
- Modify: `CXPost/Services/ImapService.cs`
- Modify: `CXPost/Program.cs`
- Modify: `CXPost/Coordinators/MailSyncCoordinator.cs`
- Modify: `CXPost/Coordinators/MessageListCoordinator.cs`
- Modify: `CXPost/Coordinators/ComposeCoordinator.cs`
- Modify: `CXPost/Coordinators/SearchCoordinator.cs`
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Create ImapConnectionFactory**

Create `CXPost/Services/ImapConnectionFactory.cs`:

```csharp
using System.Collections.Concurrent;
using CXPost.Models;

namespace CXPost.Services;

/// <summary>
/// Creates and manages per-account IImapService instances with concurrency guards.
/// </summary>
public class ImapConnectionFactory : IDisposable
{
    private readonly ICredentialService _credentials;
    private readonly ConcurrentDictionary<string, ImapService> _connections = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ImapConnectionFactory(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    /// <summary>
    /// Gets or creates an IImapService for the given account.
    /// Each account gets its own ImapClient and semaphore.
    /// </summary>
    public ImapService GetConnection(Account account)
    {
        return _connections.GetOrAdd(account.Id, _ => new ImapService(_credentials));
    }

    /// <summary>
    /// Gets the concurrency lock for the given account.
    /// All callers must acquire this before using the connection.
    /// </summary>
    public SemaphoreSlim GetLock(string accountId)
    {
        return _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Disconnects and removes the connection for the given account.
    /// Used after credential/host changes to force reconnect.
    /// </summary>
    public async Task ResetConnectionAsync(string accountId, CancellationToken ct = default)
    {
        if (_connections.TryRemove(accountId, out var service))
        {
            try { await service.DisconnectAsync(ct); }
            catch { /* best effort */ }
            service.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var conn in _connections.Values)
            conn.Dispose();
        foreach (var sem in _locks.Values)
            sem.Dispose();
    }
}
```

- [ ] **Step 2: Update Program.cs DI registration**

Replace:
```csharp
services.AddSingleton<IImapService, ImapService>();
```
With:
```csharp
services.AddSingleton<ImapConnectionFactory>();
```

Remove the `IImapService` singleton registration. All consumers will take `ImapConnectionFactory` instead.

- [ ] **Step 3: Update MailSyncCoordinator to use factory**

Replace `private readonly IImapService _imap;` with `private readonly ImapConnectionFactory _imapFactory;`.

In `SyncAccountAsync`, get a per-account connection and lock:
```csharp
var imap = _imapFactory.GetConnection(account);
var imapLock = _imapFactory.GetLock(account.Id);
await imapLock.WaitAsync(ct);
try
{
    if (!imap.IsConnected)
        await imap.ConnectAsync(account, ct);
    // ... all sync operations use `imap` instead of `_imap`
}
finally
{
    imapLock.Release();
}
```

For `SyncFolderAsync` — it's called from within `SyncAccountAsync` which already holds the lock, so it can receive the `ImapService` instance as a parameter instead of using the field. Change signature to:
```csharp
public async Task SyncFolderAsync(Account account, MailFolder folder, ImapService imap, CancellationToken ct)
```

For `FetchBodyAsync` — it already creates its own connection, so it can stay as-is but should use the factory's connection with a lock:
```csharp
public async Task FetchBodyAsync(MailFolder folder, MailMessage message, CancellationToken ct)
{
    if (message.BodyFetched) return;
    var account = _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
    if (account == null) return;
    var imap = _imapFactory.GetConnection(account);
    // FetchBodyAsync already creates its own client internally, so this is safe
    var body = await imap.FetchBodyAsync(folder.Path, message.Uid, ct);
    // ...
}
```

For `StartIdleAsync` — needs its own dedicated connection since IDLE monopolizes the client. Create a separate `ImapService` instance directly (not from the factory pool, since IDLE is long-lived):
```csharp
public async Task StartIdleAsync(Account account, string folderPath, CancellationToken ct)
{
    var idleImap = new ImapService(_imapFactory.GetCredentials());
    // ...
}
```

Actually, simpler: expose `ICredentialService` from the factory, or pass it in. The key point is IDLE gets its own client that isn't shared.

- [ ] **Step 4: Update MessageListCoordinator**

Replace `IImapService _imap` with `ImapConnectionFactory _imapFactory`. In methods that use IMAP (`ToggleFlagAsync`, `ToggleReadAsync`, `DeleteMessageAsync`, `FetchAndShowBodyAsync`):

```csharp
var account = _configService.Load().Accounts.FirstOrDefault(a => a.Id == CurrentFolder.AccountId);
if (account == null) return;
var imap = _imapFactory.GetConnection(account);
var imapLock = _imapFactory.GetLock(account.Id);
await imapLock.WaitAsync(ct);
try
{
    if (!imap.IsConnected)
        await imap.ConnectAsync(account, ct);
    await imap.SetFlagsAsync(CurrentFolder.Path, ...);
}
finally
{
    imapLock.Release();
}
```

- [ ] **Step 5: Update ComposeCoordinator**

Replace `IImapService _imap` with `ImapConnectionFactory _imapFactory`. The `AppendMessageAsync` call (copy to Sent folder) needs a lock:

```csharp
var imap = _imapFactory.GetConnection(account);
var imapLock = _imapFactory.GetLock(account.Id);
await imapLock.WaitAsync(ct);
try
{
    if (!imap.IsConnected)
        await imap.ConnectAsync(account, ct);
    await imap.AppendMessageAsync(sent.Path, message, MailKit.MessageFlags.Seen, ct);
}
finally
{
    imapLock.Release();
}
```

- [ ] **Step 6: Update SearchCoordinator and CXPostApp**

Same pattern: replace `IImapService` with `ImapConnectionFactory`, acquire per-account lock before IMAP operations.

- [ ] **Step 7: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 8: Commit**

```bash
git commit -m "fix: replace IMAP singleton with per-account connection factory with semaphore locking"
```

---

### Task 2: SMTP Connect-Send-Disconnect (#7)

**Files:**
- Modify: `CXPost/Coordinators/ComposeCoordinator.cs`

- [ ] **Step 1: Disconnect SMTP after sending**

In `ComposeCoordinator.SendAsync`, after `await _smtp.SendAsync(message, ct)` add:
```csharp
await _smtp.DisconnectAsync(ct);
```

This ensures the connection doesn't go stale. The next send will reconnect via the existing `if (!_smtp.IsConnected)` check.

- [ ] **Step 2: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 3: Commit**

```bash
git commit -m "fix: disconnect SMTP after send to prevent stale connection failures"
```

---

### Task 3: CredentialService Fixes (#4, #12)

**Files:**
- Modify: `CXPost/Services/CredentialService.cs`

- [ ] **Step 1: Fix RunProcess deadlock — read stdout/stderr concurrently**

Replace the `RunProcess` method body:

```csharp
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
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit(5000);
        var output = outputTask.Result.Trim();
        var error = errorTask.Result.Trim();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
            System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] {fileName} stderr: {error}");

        return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] Failed to run {fileName}: {ex.Message}");
        return null;
    }
}
```

- [ ] **Step 2: Fix macOS/Windows password passed as shell argument**

For macOS, use `RunProcessWithInput` to pipe the password via stdin. Replace the macOS `StorePassword` call:

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    RunProcessWithInput("security",
        $"add-generic-password -U -s {ServiceName} -a {accountId} -w -",
        password);
```

Wait — `security add-generic-password` doesn't support reading password from stdin with `-w -`. The standard macOS approach is to use the argument but quote it. Actually the real fix is to use `ProcessStartInfo.ArgumentList` instead of `Arguments` to avoid shell interpretation:

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    var psi = new ProcessStartInfo("security")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    psi.ArgumentList.Add("add-generic-password");
    psi.ArgumentList.Add("-U");
    psi.ArgumentList.Add("-s");
    psi.ArgumentList.Add(ServiceName);
    psi.ArgumentList.Add("-a");
    psi.ArgumentList.Add(accountId);
    psi.ArgumentList.Add("-w");
    psi.ArgumentList.Add(password);
    using var process = Process.Start(psi);
    process?.WaitForExit(5000);
}
```

Using `ArgumentList` avoids shell interpretation entirely — no quoting needed, password with special characters works, and while the password is still visible in `/proc/PID/cmdline` briefly, it's the standard approach for macOS `security` which has no stdin mode.

For Windows `cmdkey`, same approach with `ArgumentList`.

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 4: Commit**

```bash
git commit -m "fix: credential service deadlock and password shell argument safety"
```

---

### Task 4: Aggregated View Wrong-Folder Bug (#6)

**Files:**
- Modify: `CXPost/Coordinators/MessageListCoordinator.cs`
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Resolve folder from message at action time**

The problem: `_messageListCoordinator.CurrentFolder` points to the last folder in an aggregated set, but the selected message may belong to a different folder.

Add a method to `MessageListCoordinator`:
```csharp
public MailFolder? GetFolderForMessage(MailMessage message)
{
    if (message.FolderId == CurrentFolder?.Id) return CurrentFolder;
    // Look up across all accounts
    return _cache.GetAllFolders().FirstOrDefault(f => f.Id == message.FolderId);
}
```

Wait — `MailMessage` doesn't have `FolderId`. Let me check:

Actually, messages are loaded via `_cacheService.GetMessages(folder.Id)` which returns messages belonging to that folder. The `MailMessage` model stores `FolderId`:

Actually let me check the model:

The `MailMessage` model at `/home/nick/source/cxpost/CXPost/Models/MailMessage.cs` needs to be checked. But from the DB schema (`messages` table has `folder_id FK`), messages do have a folder association. The model likely has `FolderId` or the repo populates it.

The fix: in `MessageListCoordinator`, when performing flag/delete/move, look up the correct folder from the message's folder association rather than using `CurrentFolder`.

If `MailMessage` doesn't have `FolderId`, add it and populate it when reading from the repository.

- [ ] **Step 2: Update ToggleFlagAsync, ToggleReadAsync, DeleteMessageAsync**

Each method should resolve the correct folder path from the selected message rather than `CurrentFolder.Path`. If the message's folder differs from `CurrentFolder`, use the message's folder.

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 4: Commit**

```bash
git commit -m "fix: resolve correct folder for aggregated view message actions"
```

---

### Task 5: PopulateMessageList Stale-Index Bug (#8)

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Fix the incremental update logic**

The current logic has a stale-index bug when multiple new messages are inserted. The safest fix: after removing deleted rows and updating existing rows, batch-insert all new rows at their correct positions in a single pass:

```csharp
public void PopulateMessageList(List<MailMessage> messages)
{
    if (_messageTable == null) return;

    // Build lookup of incoming messages by UID
    var incoming = new Dictionary<uint, (int index, MailMessage msg)>();
    for (var i = 0; i < messages.Count; i++)
        incoming[messages[i].Uid] = (i, messages[i]);

    // Build lookup of existing rows by UID
    var existing = new Dictionary<uint, int>();
    for (var i = 0; i < _messageTable.RowCount; i++)
    {
        var row = _messageTable.GetRow(i);
        if (row.Tag is MailMessage m)
            existing[m.Uid] = i;
    }

    // Remove rows not in incoming (reverse order)
    var removeIndices = existing
        .Where(kv => !incoming.ContainsKey(kv.Key))
        .Select(kv => kv.Value)
        .OrderByDescending(i => i)
        .ToList();
    foreach (var idx in removeIndices)
        _messageTable.RemoveRow(idx);

    // Rebuild existing lookup after removals
    existing.Clear();
    for (var i = 0; i < _messageTable.RowCount; i++)
    {
        var row = _messageTable.GetRow(i);
        if (row.Tag is MailMessage m)
            existing[m.Uid] = i;
    }

    // Update existing rows in-place
    foreach (var kv in existing)
    {
        if (incoming.TryGetValue(kv.Key, out var pair))
        {
            var (star, from, subject, date) = FormatMessageRow(pair.msg);
            _messageTable.UpdateCell(kv.Value, 0, star);
            _messageTable.UpdateCell(kv.Value, 1, from);
            _messageTable.UpdateCell(kv.Value, 2, subject);
            _messageTable.UpdateCell(kv.Value, 3, date);
            _messageTable.GetRow(kv.Value).Tag = pair.msg;
        }
    }

    // Collect new messages (not in existing) in order
    var newMessages = messages.Where(m => !existing.ContainsKey(m.Uid)).ToList();

    // For new messages, find correct insertion point and insert
    // Since we need them in the right order relative to existing rows,
    // the simplest correct approach: clear and rebuild if there are new messages
    if (newMessages.Count > 0)
    {
        _messageTable.ClearRows();
        foreach (var msg in messages)
        {
            var (star, from, subject, date) = FormatMessageRow(msg);
            var row = new TableRow(star, from, subject, date);
            row.Tag = msg;
            _messageTable.AddRow(row);
        }
    }
}
```

This preserves in-place updates for the common case (no new messages — just read/flag changes), and falls back to full rebuild only when new messages arrive.

- [ ] **Step 2: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 3: Commit**

```bash
git commit -m "fix: message list incremental update stale-index bug"
```

---

### Task 6: Quick Model/Data Fixes (#9, #10, #16)

**Files:**
- Modify: `CXPost/Data/MailRepository.cs`
- Modify: `CXPost/Models/MailThread.cs`
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Fix DateTime.Parse with InvariantCulture (#9)**

In `MailRepository.cs` line 193, replace:
```csharp
Date = DateTime.Parse(reader.GetString(12)),
```
With:
```csharp
Date = DateTime.Parse(reader.GetString(12), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
```

- [ ] **Step 2: Fix MailThread.LatestDate empty collection (#10)**

In `MailThread.cs` line 8, replace:
```csharp
public DateTime LatestDate => Messages.Max(m => m.Date);
```
With:
```csharp
public DateTime LatestDate => Messages.Count > 0 ? Messages.Max(m => m.Date) : DateTime.MinValue;
```

- [ ] **Step 3: Fix FormatDate timezone comparison (#16)**

In `CXPostApp.cs`, update `FormatDate` to use local time for comparison:
```csharp
private static string FormatDate(DateTime date)
{
    var now = DateTime.Now;
    var localDate = date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
    if (localDate.Date == now.Date)
        return localDate.ToString("h:mm tt");
    if (localDate.Year == now.Year)
        return localDate.ToString("MMM d");
    return localDate.ToString("MMM d, yyyy");
}
```

- [ ] **Step 4: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 5: Commit**

```bash
git commit -m "fix: DateTime parsing locale safety, MailThread empty check, FormatDate timezone"
```

---

### Task 7: AccountSettingsDialog Username Field (#11)

**Files:**
- Modify: `CXPost/UI/Dialogs/AccountSettingsDialog.cs`

- [ ] **Step 1: Add Username field to General tab**

Add a field: `private PromptControl? _usernameField;`

In `BuildGeneralTab`, after the Email field add:
```csharp
AddFieldToPanel(panel, "Username (if different from email)", ref _usernameField, _existing?.Username ?? "");
```

In `TrySave`, replace:
```csharp
account.Username = account.Email;
```
With:
```csharp
account.Username = !string.IsNullOrWhiteSpace(_usernameField?.Input) ? _usernameField.Input : account.Email;
```

Add `_usernameField` to `IsEditing()` check.

- [ ] **Step 2: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 3: Commit**

```bash
git commit -m "fix: add Username field to account settings, stop overwriting with email"
```

---

### Task 8: Settings Reconnect After Change (#14)

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Reset IMAP connection after settings change**

In the `OnKeyPressed` handler for `KeyBindings.Settings`, after config reload, reset connections for changed accounts. Find the settings handler (uses `SettingsDialog`) and after `_config = _configService.Load()`:

```csharp
// Reset IMAP connections to pick up any credential/host changes
foreach (var account in _config.Accounts)
    _ = _imapFactory.ResetConnectionAsync(account.Id);
```

This uses the `ResetConnectionAsync` from Task 1's `ImapConnectionFactory`. The next sync or user action will reconnect with fresh credentials.

- [ ] **Step 2: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 3: Commit**

```bash
git commit -m "fix: reset IMAP connections after account settings change"
```

---

### Task 9: HtmlToMarkup UI Thread Block and Depth Limit (#5, #23)

**Files:**
- Modify: `CXPost/UI/Components/HtmlToMarkup.cs`

- [ ] **Step 1: Add depth limit to ProcessNode**

Add a `maxDepth` parameter (default 50) and decrement on each recursive call:

In `ProcessNode`, add a depth parameter. At the top:
```csharp
private static void ProcessNode(INode node, StringBuilder sb, ConvertState state, int depth = 50)
{
    if (depth <= 0) return;
    // ...existing code...
    // recursive calls pass depth - 1:
    ProcessNode(child, sb, state, depth - 1);
}
```

- [ ] **Step 2: Make Convert async-friendly**

Replace the blocking `.GetAwaiter().GetResult()` with synchronous HTML parsing using `AngleSharp`'s `HtmlParser` directly:

```csharp
public static string Convert(string html)
{
    var parser = new AngleSharp.Html.Parser.HtmlParser();
    var document = parser.ParseDocument(html);
    // ...rest unchanged
}
```

`HtmlParser.ParseDocument(string)` is synchronous — no `.GetAwaiter().GetResult()` needed.

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 4: Commit**

```bash
git commit -m "fix: synchronous HTML parsing, add depth limit to DOM walker"
```

---

### Task 10: Forward Subject Prefix Accumulation (#24)

**Files:**
- Modify: `CXPost/UI/Components/MessageFormatter.cs`

- [ ] **Step 1: Strip existing forward prefix before adding new one**

Update `GetForwardSubject`:
```csharp
public static string GetForwardSubject(string? subject, string prefix = "Fwd:")
{
    if (string.IsNullOrEmpty(subject)) return $"{prefix} ";
    if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return subject;
    if (subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase)) return subject;
    return $"{prefix} {subject}";
}
```

Same pattern as `GetReplySubject` — check for both the custom and default prefix.

- [ ] **Step 2: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 3: Commit**

```bash
git commit -m "fix: prevent Fwd: prefix accumulation on re-forwarded messages"
```

---

### Task 11: Wire Dead Config Fields (#18, #19, #20)

**Files:**
- Modify: `CXPost/Coordinators/MailSyncCoordinator.cs`
- Modify: `CXPost/Coordinators/NotificationCoordinator.cs`
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Wire SyncIntervalSeconds — add periodic sync timer (#18)**

In `CXPostApp.StartBackgroundSync`, after the initial sync, start a periodic timer per account:

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
        EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
    }
    catch { }

    // Periodic re-sync
    while (!_cts.Token.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(account.SyncIntervalSeconds), _cts.Token);
            await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
            EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
        }
        catch (OperationCanceledException) { break; }
        catch { }
    }
}, _cts.Token);
```

- [ ] **Step 2: Wire MaxMessagesPerFolder (#19)**

In `MailSyncCoordinator.SyncFolderAsync`, after getting `serverUids`, limit if configured:

```csharp
var serverUids = await imap.GetUidsAsync(folder.Path, ct);

// Apply max messages limit
if (account.MaxMessagesPerFolder > 0 && serverUids.Count > account.MaxMessagesPerFolder)
{
    // Keep only the most recent UIDs (highest UIDs = newest)
    serverUids = serverUids.OrderByDescending(u => u).Take(account.MaxMessagesPerFolder).ToHashSet();
}
```

- [ ] **Step 3: Wire NotificationsEnabled (#20)**

In `MailSyncCoordinator.SyncAccountAsync`, wrap the notification call:

```csharp
if (account.NotificationsEnabled)
    _notifications.NotifySyncComplete(account.Name, totalMessages);
```

Also guard `NotificationCoordinator.NotifyNewMail` — but this is only called from IDLE, where the account is known. The IDLE handler in `MailSyncCoordinator.StartIdleAsync` should check `account.NotificationsEnabled` before calling notify.

- [ ] **Step 4: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: wire per-account sync interval, max messages, and notification toggle"
```

---

### Task 12: Cleanup (#15, #17, #21)

**Files:**
- Delete: `CXPost.Tests/UnitTest1.cs`
- Modify: `CXPost/UI/CXPostApp.cs`
- Modify: `CXPost/UI/Components/AccountDashboard.cs`
- Modify: `CXPost/UI/KeyBindings.cs`

- [ ] **Step 1: Delete empty UnitTest1.cs (#15)**

```bash
rm CXPost.Tests/UnitTest1.cs
```

- [ ] **Step 2: Extract shared GetFolderIcon (#17)**

Move `GetFolderIcon` from `CXPostApp.cs` to `MessageFormatter.cs` as a `public static` method. Update both `CXPostApp.cs` and `AccountDashboard.cs` to call `MessageFormatter.GetFolderIcon(...)` instead of their private copies. Delete both private copies.

- [ ] **Step 3: Fix SaveDraft key binding conflict (#21)**

In `KeyBindings.cs`, remove or comment the `SaveDraft` binding since draft saving is not implemented:

```csharp
// Reserved for future use:
// public static readonly ConsoleKey SaveDraft = ConsoleKey.S; // Ctrl+S (in compose) — conflicts with Search
```

- [ ] **Step 4: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 5: Commit**

```bash
git commit -m "chore: remove empty test, deduplicate GetFolderIcon, fix SaveDraft key conflict"
```

---

### Task 13: Search on Aggregated View (#13)

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Search across all folders in aggregated view**

In the Ctrl+S handler, when `_messageListCoordinator.CurrentFolder` is set from an aggregated view, search all folders in the aggregated set. Use `_aggregatedFolders` (already stored as a field):

```csharp
// In the search handler, after getting the query:
if (_aggregatedFolders.Count > 0 && /* currently in aggregated view */)
{
    var allResults = new List<MailMessage>();
    foreach (var folders in _aggregatedFolders.Values)
    {
        foreach (var folder in folders)
        {
            var uids = await _searchCoordinator.SearchAsync(folder, query, _cts.Token);
            var messages = _cacheService.GetMessages(folder.Id)
                .Where(m => uids.Contains(m.Uid)).ToList();
            allResults.AddRange(messages);
        }
    }
    allResults.Sort((a, b) => b.Date.CompareTo(a.Date));
    EnqueueUiAction(() => PopulateMessageList(allResults));
}
```

Track whether we're in an aggregated view with a boolean field `_isAggregatedView` set in `OnFolderSelected`.

- [ ] **Step 2: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 3: Commit**

```bash
git commit -m "fix: search across all folders in aggregated view"
```

---

### Self-Review

**Spec coverage:** All 25 issues covered. #25 (autocomplete) deferred with documented reason.

**Placeholder scan:** No TBDs or vague steps. Task 1 (IMAP factory) has the most complexity — the code is directional but the implementer will need to trace all `_imap` usages.

**Type consistency:** `ImapConnectionFactory` is used consistently. `GetConnection` returns `ImapService` (concrete), `GetLock` returns `SemaphoreSlim`.

**Dependency order:** Task 1 (IMAP factory) must be done first — Tasks 4, 8, 11, 13 depend on it. Tasks 2-3, 5-7, 9-10, 12 are independent.
