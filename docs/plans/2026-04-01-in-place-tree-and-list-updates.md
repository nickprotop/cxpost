# In-Place Folder Tree & Message List Updates

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace destructive clear-and-rebuild of the folder tree and message list with in-place updates that preserve selection, expansion state, and avoid visual flicker during sync.

**Architecture:** Both `PopulateFolderTree` and `PopulateMessageList` currently call `.Clear()`/`.ClearRows()` and rebuild from scratch. Replace each with a diff-based approach: walk existing nodes/rows, update text for changed items, add new items, remove deleted items. Tag-based identity matching (folder ID, account ID, message UID) drives the diff. Selection and expansion state are untouched because nodes/rows are never removed and re-added unless structurally changed.

**Tech Stack:** .NET 10, SharpConsoleUI (ConsoleEx TreeControl, TableControl)

---

## Available ConsoleEx APIs

**TreeControl:**
- `RootNodes` — `ReadOnlyCollection<TreeNode>`
- `SelectedNode` — get/set
- `FindNodeByTag(object)` — recursive search
- `AddRootNode(string)` — returns `TreeNode`
- `RemoveRootNode(TreeNode)`
- `Clear()` — removes all

**TreeNode:**
- `Text` — get/set (triggers repaint)
- `Tag` — get/set
- `TextColor` — get/set
- `IsExpanded` — get/set
- `Children` — `ReadOnlyCollection<TreeNode>`
- `AddChild(string)` — returns `TreeNode`
- `RemoveChild(TreeNode)`
- `ClearChildren()`

**TableControl:**
- `RowCount`, `GetRow(index)`, `SelectedRowIndex`
- `AddRow(TableRow)`, `InsertRow(index, TableRow)`, `InsertRows(index, IEnumerable<TableRow>)`
- `RemoveRow(index)` — adjusts selection automatically
- `UpdateCell(row, column, value)` — in-place cell update
- `ClearRows()`

**TableRow:**
- `Cells` — `List<string>` (mutable)
- `Tag` — `object?`

---

### Task 1: Refactor PopulateFolderTree to In-Place Update

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs` — method `PopulateFolderTree` (~lines 545-634)

**Current behavior:** `_folderTree.Clear()` then rebuild all nodes from scratch.

**New behavior:** Diff existing tree against current data, update text in-place, add/remove only structural changes.

- [ ] **Step 1: Extract a helper to generate node text**

Add a private method so text generation is consistent between initial build and updates:

```csharp
private static string FormatFolderNodeText(string icon, string displayName, int unread, int total)
{
    if (unread > 0)
        return $"{icon} {MarkupParser.Escape(displayName)} [yellow]({unread})[/]";
    if (total > 0)
        return $"{icon} {MarkupParser.Escape(displayName)} [grey35]({total})[/]";
    return $"[grey70]{icon} {MarkupParser.Escape(displayName)}[/]";
}
```

- [ ] **Step 2: Rewrite PopulateFolderTree with in-place updates**

The new logic:

```
1. Gather data (same as before — foldersByType, allAccountFolders)
2. "All Accounts" root node:
   a. If not found by tag "all-accounts", create it (first run)
   b. For each folder type: find child by tag (type string), update text or add new
   c. Remove children whose type no longer exists
3. Per-account root nodes:
   a. If account root not found by tag (Account with matching Id), create it
   b. For each folder: find child by tag (MailFolder with matching Id), update text or add new
   c. Remove children whose folder no longer exists
4. Remove root nodes for accounts that no longer exist
5. Invalidate tree
```

Key implementation details:

- **Identity matching:** Use `FindNodeByTag` for root-level lookups. For children, iterate `node.Children` and match by tag.
- **Aggregated folder children** under "All Accounts": use the folder type string as tag (e.g., "Inbox", "Sent") instead of the `List<MailFolder>`, so we can find them. Store the `List<MailFolder>` on a separate property or update tag after text update.
- **Account root nodes:** Tag with account ID string (not the Account object) so equality works across config reloads.
- **Folder nodes:** Tag with folder ID string for the same reason.
- **First run:** When `_folderTree.RootNodes.Count == 0`, fall through to full build (all adds, no diffs).

```csharp
public void PopulateFolderTree()
{
    if (_folderTree == null) return;

    // ── Gather data ─────────────────────────────────────────────────
    var foldersByType = new Dictionary<string, List<MailFolder>>(StringComparer.OrdinalIgnoreCase);
    var allAccountFolders = new List<(Account account, List<MailFolder> folders)>();

    foreach (var account in _config.Accounts)
    {
        var folders = _cacheService.GetFolders(account.Id);
        allAccountFolders.Add((account, folders));
        foreach (var folder in folders)
        {
            if (folder.DisplayName.StartsWith("[") && folder.DisplayName.EndsWith("]"))
                continue;
            var key = NormalizeFolderType(folder.DisplayName, folder.Path);
            if (!foldersByType.ContainsKey(key))
                foldersByType[key] = [];
            foldersByType[key].Add(folder);
        }
    }

    // ── "All Accounts" node ─────────────────────────────────────────
    var allNode = _folderTree.FindNodeByTag("all-accounts");
    if (allNode == null)
    {
        allNode = _folderTree.AddRootNode("\U0001f4ec All Accounts");
        allNode.TextColor = ColorScheme.PrimaryText;
        allNode.Tag = "all-accounts";
    }

    // Update or add aggregated type children
    var existingTypeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var child in allNode.Children.ToList())
    {
        if (child.Tag is string typeKey)
            existingTypeKeys.Add(typeKey);
    }

    var currentTypeKeys = new HashSet<string>(foldersByType.Keys, StringComparer.OrdinalIgnoreCase);

    // Remove types that no longer exist
    foreach (var child in allNode.Children.ToList())
    {
        if (child.Tag is string typeKey && !currentTypeKeys.Contains(typeKey))
            allNode.RemoveChild(child);
    }

    // Update or add types
    foreach (var type in foldersByType.Keys.OrderBy(FolderSortKey))
    {
        var typeFolders = foldersByType[type];
        var icon = GetFolderIcon(type);
        var unread = 0;
        var total = 0;
        foreach (var f in typeFolders)
        {
            var msgs = _cacheService.GetMessages(f.Id);
            unread += msgs.Count(m => !m.IsRead);
            total += msgs.Count;
        }

        var text = FormatFolderNodeText(icon, type, unread, total);

        // Find existing child for this type
        TreeNode? typeNode = null;
        foreach (var child in allNode.Children)
        {
            if (child.Tag is string childKey && childKey.Equals(type, StringComparison.OrdinalIgnoreCase))
            { typeNode = child; break; }
        }

        if (typeNode != null)
        {
            typeNode.Text = text;
        }
        else
        {
            var newChild = allNode.AddChild(text);
            newChild.Tag = type;  // tag with type string for identity
        }
    }

    // ── Per-account nodes ───────────────────────────────────────────
    var currentAccountIds = new HashSet<string>(_config.Accounts.Select(a => a.Id));

    // Remove account root nodes that no longer exist
    foreach (var rootNode in _folderTree.RootNodes.ToList())
    {
        if (rootNode.Tag is string acctId && acctId != "all-accounts" && !currentAccountIds.Contains(acctId))
            _folderTree.RemoveRootNode(rootNode);
    }

    foreach (var (account, folders) in allAccountFolders)
    {
        // Find or create account root node
        var accountNode = _folderTree.FindNodeByTag(account.Id);
        if (accountNode == null)
        {
            accountNode = _folderTree.AddRootNode($"[grey50 bold]{MarkupParser.Escape(account.Name.ToUpperInvariant())}[/]");
            accountNode.TextColor = ColorScheme.MutedText;
            accountNode.Tag = account.Id;
        }

        var validFolders = folders
            .Where(f => !f.DisplayName.StartsWith("[") || !f.DisplayName.EndsWith("]"))
            .OrderBy(f => FolderSortKey(f.DisplayName)).ThenBy(f => f.Path)
            .ToList();

        var currentFolderIds = new HashSet<string>(validFolders.Select(f => f.Id));

        // Remove folder children that no longer exist
        foreach (var child in accountNode.Children.ToList())
        {
            if (child.Tag is string folderId && !currentFolderIds.Contains(folderId))
                accountNode.RemoveChild(child);
        }

        // Update or add folder children
        foreach (var folder in validFolders)
        {
            var icon = GetFolderIcon(folder.DisplayName);
            var msgs = _cacheService.GetMessages(folder.Id);
            var unread = msgs.Count(m => !m.IsRead);
            var total = msgs.Count;
            var text = FormatFolderNodeText(icon, folder.DisplayName, unread, total);

            TreeNode? folderNode = null;
            foreach (var child in accountNode.Children)
            {
                if (child.Tag is string childFolderId && childFolderId == folder.Id)
                { folderNode = child; break; }
            }

            if (folderNode != null)
            {
                folderNode.Text = text;
            }
            else
            {
                var newChild = accountNode.AddChild(text);
                newChild.Tag = folder.Id;
            }
        }
    }

    _folderTree.Invalidate();
}
```

**Important tag changes from current code:**
- "All Accounts" node: `Tag = "all-accounts"` (was `"all-accounts"` — unchanged)
- Aggregated type nodes: `Tag = typeString` (was `List<MailFolder>` — CHANGED)
- Account root nodes: `Tag = account.Id` (was `Account` object — CHANGED)
- Folder nodes: `Tag = folder.Id` (was `MailFolder` object — CHANGED)

These tag changes affect `OnFolderSelected` which reads `args.Node.Tag` to determine what was clicked.

- [ ] **Step 3: Update OnFolderSelected to handle new tag types**

The current `OnFolderSelected` checks `Tag is Account`, `Tag is MailFolder`, `Tag is List<MailFolder>`, `Tag is string "all-accounts"`. Update to:

- `Tag is string "all-accounts"` → show all-accounts dashboard (unchanged)
- `Tag is string typeKey` (aggregated type under All Accounts) → look up the `List<MailFolder>` from the gathered data instead of storing it on the tag. Or: after updating node text, also update a separate lookup. Simplest: keep a `Dictionary<string, List<MailFolder>> _aggregatedFoldersByType` field on the app, refreshed during `PopulateFolderTree`, and look it up in `OnFolderSelected`.
- `Tag is string accountId` (account root) → look up account from `_config.Accounts.FirstOrDefault(a => a.Id == accountId)` and show account dashboard
- `Tag is string folderId` (folder node) → look up folder from cache

Since all tags are now strings, distinguish them by checking which dictionary/list they belong to, or use a wrapper. **Simplest approach:** use typed tag wrappers:

```csharp
private record FolderTag(string FolderId);
private record AccountTag(string AccountId);
private record AggregatedTag(string TypeKey);
```

Then tags become: `"all-accounts"`, `new AccountTag(account.Id)`, `new AggregatedTag(type)`, `new FolderTag(folder.Id)`. FindNodeByTag still works with record equality. OnFolderSelected pattern-matches cleanly.

- [ ] **Step 4: Update OnFolderSelected pattern matching**

```csharp
private void OnFolderSelected(object? sender, TreeNodeEventArgs args)
{
    var tag = args.Node?.Tag;
    if (tag is string s && s == "all-accounts")
    {
        // Show all-accounts dashboard
        ...
    }
    else if (tag is AggregatedTag agg)
    {
        // Look up folders from _aggregatedFolders[agg.TypeKey]
        ...
    }
    else if (tag is AccountTag acct)
    {
        var account = _config.Accounts.FirstOrDefault(a => a.Id == acct.AccountId);
        if (account != null) { /* show account dashboard */ }
    }
    else if (tag is FolderTag ft)
    {
        var folder = FindFolderById(ft.FolderId);
        if (folder != null) { /* select folder, show messages */ }
    }
}
```

Add a helper to find folders:
```csharp
private MailFolder? FindFolderById(string folderId)
{
    foreach (var account in _config.Accounts)
    {
        var folder = _cacheService.GetFolders(account.Id).FirstOrDefault(f => f.Id == folderId);
        if (folder != null) return folder;
    }
    return null;
}
```

- [ ] **Step 5: Store aggregated folders lookup**

Add a field:
```csharp
private Dictionary<string, List<MailFolder>> _aggregatedFolders = new(StringComparer.OrdinalIgnoreCase);
```

Populate it at the start of `PopulateFolderTree` (it's already computed as `foldersByType`):
```csharp
_aggregatedFolders = foldersByType;
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Run all tests**

Run: `dotnet test`
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add CXPost/UI/CXPostApp.cs
git commit -m "refactor: in-place folder tree updates preserving selection and expansion"
```

---

### Task 2: Refactor PopulateMessageList to In-Place Update

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs` — method `PopulateMessageList` (~lines 831-851)

**Current behavior:** `_messageTable.ClearRows()` then add all rows.

**New behavior:** Diff existing rows (by UID via Tag) against incoming messages. Update changed cells, insert new rows, remove deleted rows.

- [ ] **Step 1: Extract helper for building row cells**

```csharp
private static (string star, string from, string subject, string date) FormatMessageRow(MailMessage msg)
{
    var star = msg.IsFlagged ? "[yellow]\u2605[/]" : "[grey35]\u2606[/]";
    var from = msg.IsRead
        ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]"
        : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]";
    var subject = msg.IsRead
        ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]"
        : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]";
    var date = FormatDate(msg.Date);
    return (star, from, subject, date);
}
```

- [ ] **Step 2: Rewrite PopulateMessageList with diff-based updates**

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

    // Remove rows not in incoming (iterate in reverse to keep indices stable)
    var toRemove = existing.Keys.Where(uid => !incoming.ContainsKey(uid)).ToList();
    var removeIndices = toRemove.Select(uid => existing[uid]).OrderByDescending(i => i).ToList();
    foreach (var idx in removeIndices)
        _messageTable.RemoveRow(idx);

    // After removals, rebuild existing lookup (indices shifted)
    existing.Clear();
    for (var i = 0; i < _messageTable.RowCount; i++)
    {
        var row = _messageTable.GetRow(i);
        if (row.Tag is MailMessage m)
            existing[m.Uid] = i;
    }

    // Update existing rows and insert new ones
    for (var i = 0; i < messages.Count; i++)
    {
        var msg = messages[i];
        var (star, from, subject, date) = FormatMessageRow(msg);

        if (existing.TryGetValue(msg.Uid, out var rowIdx))
        {
            // Update cells in-place
            _messageTable.UpdateCell(rowIdx, 0, star);
            _messageTable.UpdateCell(rowIdx, 1, from);
            _messageTable.UpdateCell(rowIdx, 2, subject);
            _messageTable.UpdateCell(rowIdx, 3, date);
            // Update tag reference (message object may have changed)
            _messageTable.GetRow(rowIdx).Tag = msg;
        }
        else
        {
            // New message — insert at correct position
            var row = new TableRow(star, from, subject, date);
            row.Tag = msg;
            _messageTable.InsertRow(i, row);
        }
    }
}
```

**Note:** This approach handles:
- Read/unread toggle → cells update in-place with different markup
- Flag toggle → star cell updates in-place
- New messages from sync → inserted at correct position
- Deleted messages → removed without clearing selection
- Selection is preserved because `RemoveRow` and `InsertRow` adjust `SelectedRowIndex` automatically

- [ ] **Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Run all tests**

Run: `dotnet test`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add CXPost/UI/CXPostApp.cs
git commit -m "refactor: in-place message list updates preserving selection"
```

---

### Task 3: Final Integration Verification

- [ ] **Step 1: Build clean**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests PASS.

- [ ] **Step 3: Verify no references to old tag types**

Search for `Tag is Account`, `Tag is MailFolder`, `Tag is List<MailFolder>` — should be zero matches (all replaced with record tag wrappers).

Run: `grep -rn "Tag is Account\|Tag is MailFolder\|Tag is List<MailFolder>" CXPost/`
Expected: No matches.

- [ ] **Step 4: Final commit if cleanup needed**

```bash
git add -A
git commit -m "chore: final cleanup for in-place tree and list updates"
```
