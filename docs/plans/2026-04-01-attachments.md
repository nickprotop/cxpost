# Attachments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full attachment support — view attachment metadata, save to disk, and send emails with file attachments.

**Architecture:** `AttachmentInfo` model stores metadata in-memory (not DB). `ImapService.FetchBodyAsync` returns body + attachment list. `ShowMessagePreview` renders clickable attachment controls in the reading pane. `ComposeDialog` adds F2 file picker with attachment list. `ComposeCoordinator.SendAsync` builds multipart MIME when attachments present.

**Tech Stack:** .NET 10, MailKit/MimeKit, SharpConsoleUI

---

### Task 1: AttachmentInfo Model + FetchBodyAsync Extraction

**Files:**
- Create: `CXPost/Models/AttachmentInfo.cs`
- Modify: `CXPost/Models/MailMessage.cs`
- Modify: `CXPost/Services/ImapService.cs`
- Modify: `CXPost/Coordinators/MailSyncCoordinator.cs`

- [ ] **Step 1: Create AttachmentInfo model**

Create `CXPost/Models/AttachmentInfo.cs`:

```csharp
namespace CXPost.Models;

public class AttachmentInfo
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string MimeType { get; set; } = "application/octet-stream";
    public int Index { get; set; }
}
```

- [ ] **Step 2: Add Attachments property to MailMessage**

In `CXPost/Models/MailMessage.cs`, add after `BodyFetched`:

```csharp
public List<AttachmentInfo>? Attachments { get; set; }
```

- [ ] **Step 3: Update ImapService.FetchBodyAsync to extract attachment metadata**

In `CXPost/Services/ImapService.cs`, change `FetchBodyAsync` return type from `Task<string?>` to `Task<(string? body, List<AttachmentInfo> attachments)>`:

```csharp
public async Task<(string? body, List<AttachmentInfo> attachments)> FetchBodyAsync(
    string folderPath, uint uid, CancellationToken ct = default)
{
    if (_account == null)
        throw new InvalidOperationException("No account configured. Call ConnectAsync first.");

    using var client = new ImapClient();
    var socketOptions = _account.ImapSecurity switch
    {
        SecurityType.Ssl => SecureSocketOptions.SslOnConnect,
        SecurityType.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None
    };
    await client.ConnectAsync(_account.ImapHost, _account.ImapPort, socketOptions, ct);

    var password = _credentials.GetPassword(_account.Id) ?? string.Empty;
    await client.AuthenticateAsync(
        _account.Username.Length > 0 ? _account.Username : _account.Email,
        password, ct);

    var folder = await client.GetFolderAsync(folderPath, ct);
    await folder.OpenAsync(FolderAccess.ReadOnly, ct);

    var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
    await folder.CloseAsync(false, ct);
    await client.DisconnectAsync(true, ct);

    // Extract attachment metadata
    var attachments = new List<AttachmentInfo>();
    var index = 0;
    foreach (var attachment in message.Attachments)
    {
        var fileName = attachment is MimePart mp ? mp.FileName : null;
        long size = 0;
        if (attachment is MimePart mp2 && mp2.Content?.Stream != null)
        {
            try { size = mp2.Content.Stream.Length; }
            catch { /* stream may not support Length */ }
        }
        attachments.Add(new AttachmentInfo
        {
            FileName = fileName ?? $"attachment_{index}",
            Size = size,
            MimeType = attachment.ContentType?.MimeType ?? "application/octet-stream",
            Index = index
        });
        index++;
    }

    return (message.HtmlBody ?? message.TextBody, attachments);
}
```

Update `IImapService` interface to match the new return type.

- [ ] **Step 4: Update MailSyncCoordinator.FetchBodyAsync to pass attachments**

In `CXPost/Coordinators/MailSyncCoordinator.cs`, update `FetchBodyAsync`:

```csharp
public async Task FetchBodyAsync(MailFolder folder, MailMessage message, CancellationToken ct)
{
    if (message.BodyFetched) return;

    var account = _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
    if (account == null) return;
    var imap = _imapFactory.GetConnection(account);
    var (body, attachments) = await imap.FetchBodyAsync(folder.Path, message.Uid, ct);
    if (body != null)
    {
        _cache.StoreBody(folder.Id, message.Uid, body);
        message.BodyPlain = body;
        message.BodyFetched = true;
        message.Attachments = attachments.Count > 0 ? attachments : null;
    }
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 6: Commit**

```bash
git commit -m "feat: extract attachment metadata during body fetch"
```

---

### Task 2: Add SaveAttachmentAsync to ImapService

**Files:**
- Modify: `CXPost/Services/ImapService.cs`
- Modify: `CXPost/Services/IImapService.cs`

- [ ] **Step 1: Add SaveAttachmentAsync method**

In `CXPost/Services/ImapService.cs`, add:

```csharp
public async Task SaveAttachmentAsync(string folderPath, uint uid, int attachmentIndex,
    string targetPath, CancellationToken ct = default)
{
    if (_account == null)
        throw new InvalidOperationException("No account configured. Call ConnectAsync first.");

    using var client = new ImapClient();
    var socketOptions = _account.ImapSecurity switch
    {
        SecurityType.Ssl => SecureSocketOptions.SslOnConnect,
        SecurityType.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None
    };
    await client.ConnectAsync(_account.ImapHost, _account.ImapPort, socketOptions, ct);

    var password = _credentials.GetPassword(_account.Id) ?? string.Empty;
    await client.AuthenticateAsync(
        _account.Username.Length > 0 ? _account.Username : _account.Email,
        password, ct);

    var folder = await client.GetFolderAsync(folderPath, ct);
    await folder.OpenAsync(FolderAccess.ReadOnly, ct);

    var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
    await folder.CloseAsync(false, ct);
    await client.DisconnectAsync(true, ct);

    var attachments = message.Attachments.ToList();
    if (attachmentIndex < 0 || attachmentIndex >= attachments.Count)
        throw new ArgumentOutOfRangeException(nameof(attachmentIndex));

    var attachment = attachments[attachmentIndex];
    if (attachment is MimePart part)
    {
        // Handle duplicate filenames
        var finalPath = GetUniqueFilePath(targetPath);
        var dir = Path.GetDirectoryName(finalPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var stream = File.Create(finalPath);
        await part.Content.DecodeToAsync(stream, ct);
    }
}

private static string GetUniqueFilePath(string path)
{
    if (!File.Exists(path)) return path;
    var dir = Path.GetDirectoryName(path) ?? ".";
    var name = Path.GetFileNameWithoutExtension(path);
    var ext = Path.GetExtension(path);
    var counter = 1;
    string candidate;
    do
    {
        candidate = Path.Combine(dir, $"{name} ({counter}){ext}");
        counter++;
    } while (File.Exists(candidate));
    return candidate;
}
```

- [ ] **Step 2: Add to IImapService interface**

```csharp
Task SaveAttachmentAsync(string folderPath, uint uid, int attachmentIndex, string targetPath, CancellationToken ct = default);
```

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: add SaveAttachmentAsync for downloading attachments to disk"
```

---

### Task 3: Message List — Attachment Column

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Add clip column to table definition**

In `CreateLayout()`, find the `_messageTable` builder and add a column after the star column:

```csharp
_messageTable = Controls.Table()
    .AddColumn("\u2605", TextJustification.Center, width: 3)
    .AddColumn("\U0001f4ce", TextJustification.Center, width: 3)  // 📎
    .AddColumn("From", width: 24)
    .AddColumn("Subject")
    .AddColumn("Date", TextJustification.Right, width: 12)
    // ... rest unchanged
```

- [ ] **Step 2: Update FormatMessageRow to include clip column**

```csharp
private static (string star, string clip, string from, string subject, string date) FormatMessageRow(MailMessage msg)
{
    var star = msg.IsFlagged ? "[yellow]\u2605[/]" : "[grey35]\u2606[/]";
    var clip = msg.HasAttachments ? "[grey70]\U0001f4ce[/]" : "";
    var from = msg.IsRead
        ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]"
        : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown")}[/]";
    var subject = msg.IsRead
        ? $"[{ColorScheme.ReadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]"
        : $"[{ColorScheme.UnreadMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]";
    var date = FormatDate(msg.Date);
    return (star, clip, from, subject, date);
}
```

- [ ] **Step 3: Update all PopulateMessageList and UpdateCell calls**

Every place that creates `TableRow` or calls `UpdateCell` needs the new column:

```csharp
// In PopulateMessageList — row creation:
var (star, clip, from, subject, date) = FormatMessageRow(msg);
var row = new TableRow(star, clip, from, subject, date) { Tag = msg };

// In PopulateMessageList — UpdateCell calls:
_messageTable.UpdateCell(kv.Value, 0, star);
_messageTable.UpdateCell(kv.Value, 1, clip);
_messageTable.UpdateCell(kv.Value, 2, from);
_messageTable.UpdateCell(kv.Value, 3, subject);
_messageTable.UpdateCell(kv.Value, 4, date);
```

- [ ] **Step 4: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: add attachment indicator column to message list"
```

---

### Task 4: Message Preview — Attachment Section with Clickable Controls

**Files:**
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Refactor ShowMessagePreview to use multiple controls in reading pane**

Currently `ShowMessagePreview` sets markup lines on `_readingContent`. Refactor to clear the reading pane and add multiple controls — header markup, attachment controls (if any), body markup:

```csharp
public void ShowMessagePreview(MailMessage msg)
{
    if (_readingPane == null || _readingContent == null) return;

    // Clear all controls from reading pane and re-add
    _readingPane.ClearContents();

    // Header markup
    var headerLines = new List<string>
    {
        "",
        $"  [{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]",
        "",
        $"  [{ColorScheme.MutedMarkup}]From:[/]  {MarkupParser.Escape(msg.FromName ?? "")} <{MarkupParser.Escape(msg.FromAddress ?? "")}>",
        $"  [{ColorScheme.MutedMarkup}]Date:[/]  {msg.Date:MMMM d, yyyy h:mm tt}",
        $"  [{ColorScheme.MutedMarkup}]To:[/]    {MarkupParser.Escape(MessageFormatter.FormatAddresses(msg.ToAddresses))}",
        ""
    };

    var headerControl = Controls.Markup().Build();
    headerControl.HorizontalAlignment = HorizontalAlignment.Stretch;
    headerControl.SetContent(headerLines);
    _readingPane.AddControl(headerControl);

    // Attachment section (if any)
    if (msg.Attachments != null && msg.Attachments.Count > 0)
    {
        AddAttachmentControls(msg);
    }

    // Body section
    if (msg.BodyFetched && msg.BodyPlain != null)
    {
        var bodyLines = new List<string>();
        bodyLines.Add($"  [grey23]{"".PadRight(60, '\u2500')}[/]");
        bodyLines.Add("");

        var body = msg.BodyPlain;
        if (MessageFormatter.IsHtml(body))
        {
            var markup = Components.HtmlToMarkup.Convert(body);
            bodyLines.AddRange(markup.Split('\n').Select(l => $"  {l}"));
        }
        else
        {
            bodyLines.AddRange(body.Split('\n').Select(l => $"  {MarkupParser.Escape(l)}"));
        }

        var bodyControl = Controls.Markup().Build();
        bodyControl.HorizontalAlignment = HorizontalAlignment.Stretch;
        bodyControl.SetContent(bodyLines);
        _readingPane.AddControl(bodyControl);
    }
    else
    {
        var loadingControl = Controls.Markup($"  [{ColorScheme.MutedMarkup}]Loading message body...[/]").Build();
        _readingPane.AddControl(loadingControl);
    }

    if (!_isSearchActive && _readingPane.CanScrollDown)
        SetRightPanelHeader("[grey70]Messages[/] [grey50](\u2191\u2193 to scroll)[/]");
}
```

- [ ] **Step 2: Add AddAttachmentControls method**

```csharp
private void AddAttachmentControls(MailMessage msg)
{
    if (_readingPane == null || msg.Attachments == null) return;

    // Attachment header
    var headerMarkup = Controls.Markup(
        $"  [{ColorScheme.PrimaryMarkup}]\U0001f4ce Attachments ({msg.Attachments.Count})[/]")
        .WithMargin(0, 0, 0, 0)
        .Build();
    headerMarkup.HorizontalAlignment = HorizontalAlignment.Stretch;
    _readingPane.AddControl(headerMarkup);

    // Rule
    var rule = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
    _readingPane.AddControl(rule);

    // Each attachment as a clickable StatusBarControl
    foreach (var att in msg.Attachments)
    {
        var sizeStr = FormatFileSize(att.Size);
        var idx = att.Index;
        var fileName = att.FileName;

        var attBar = Controls.StatusBar()
            .AddLeftText($"  [[{idx + 1}]] {MarkupParser.Escape(fileName)}  [grey50]{sizeStr}[/]", () =>
            {
                SaveAttachmentQuick(msg, idx, fileName);
            })
            .WithMargin(2, 0, 2, 0)
            .Build();
        attBar.BackgroundColor = Color.Transparent;
        _readingPane.AddControl(attBar);
    }

    // Rule after attachments
    var rule2 = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
    _readingPane.AddControl(rule2);

    // Action bar
    var actionBar = Controls.StatusBar()
        .AddLeft("S", "Save", () => SaveAttachmentQuick(msg, 0, msg.Attachments[0].FileName))
        .AddLeft("Shift+S", "Save As", () => SaveAttachmentAs(msg, 0))
        .AddLeft("A", "Save All", () => SaveAllAttachments(msg))
        .WithMargin(2, 0, 2, 0)
        .Build();
    actionBar.BackgroundColor = Color.Transparent;
    _readingPane.AddControl(actionBar);
}

private static string FormatFileSize(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
    return $"{bytes / (1024.0 * 1024.0):F1} MB";
}
```

- [ ] **Step 3: Add save methods**

```csharp
private void SaveAttachmentQuick(MailMessage msg, int index, string fileName)
{
    var folder = _messageListCoordinator.CurrentFolder;
    var account = GetCurrentAccount();
    if (folder == null || account == null) return;

    var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    var targetPath = Path.Combine(downloadsDir, fileName);

    _ = Task.Run(async () =>
    {
        try
        {
            using var imap = new ImapService(_imapFactory.Credentials);
            await imap.ConnectAsync(account, _cts.Token);
            await imap.SaveAttachmentAsync(folder.Path, msg.Uid, index, targetPath, _cts.Token);
            EnqueueUiAction(() => ShowSuccess($"Saved {fileName} to ~/Downloads/"));
        }
        catch (Exception ex)
        {
            EnqueueUiAction(() => ShowError($"Save failed: {ex.Message}"));
        }
    }, _cts.Token);
}

private void SaveAttachmentAs(MailMessage msg, int index)
{
    var folder = _messageListCoordinator.CurrentFolder;
    var account = GetCurrentAccount();
    if (folder == null || account == null || msg.Attachments == null) return;

    var fileName = msg.Attachments[index].FileName;

    _ = Task.Run(async () =>
    {
        var dir = await FileDialogs.ShowFolderPickerAsync(_ws);
        if (dir == null) return;

        var targetPath = Path.Combine(dir, fileName);
        try
        {
            using var imap = new ImapService(_imapFactory.Credentials);
            await imap.ConnectAsync(account, _cts.Token);
            await imap.SaveAttachmentAsync(folder.Path, msg.Uid, index, targetPath, _cts.Token);
            EnqueueUiAction(() => ShowSuccess($"Saved {fileName} to {dir}"));
        }
        catch (Exception ex)
        {
            EnqueueUiAction(() => ShowError($"Save failed: {ex.Message}"));
        }
    }, _cts.Token);
}

private void SaveAllAttachments(MailMessage msg)
{
    var folder = _messageListCoordinator.CurrentFolder;
    var account = GetCurrentAccount();
    if (folder == null || account == null || msg.Attachments == null) return;

    var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    _ = Task.Run(async () =>
    {
        try
        {
            using var imap = new ImapService(_imapFactory.Credentials);
            await imap.ConnectAsync(account, _cts.Token);
            foreach (var att in msg.Attachments)
            {
                var targetPath = Path.Combine(downloadsDir, att.FileName);
                await imap.SaveAttachmentAsync(folder.Path, msg.Uid, att.Index, targetPath, _cts.Token);
            }
            EnqueueUiAction(() => ShowSuccess($"Saved {msg.Attachments.Count} attachments to ~/Downloads/"));
        }
        catch (Exception ex)
        {
            EnqueueUiAction(() => ShowError($"Save failed: {ex.Message}"));
        }
    }, _cts.Token);
}
```

- [ ] **Step 4: Add keyboard shortcuts for attachments in OnKeyPressed**

In `OnKeyPressed`, add handlers for S, Shift+S, A when viewing a message with attachments (not in compose/search context):

```csharp
else if (e.KeyInfo.Key == ConsoleKey.S && !ctrl && !shift)
{
    var msg = GetSelectedMessage();
    if (msg?.Attachments != null && msg.Attachments.Count > 0)
    {
        SaveAttachmentQuick(msg, 0, msg.Attachments[0].FileName);
        e.Handled = true;
    }
}
else if (e.KeyInfo.Key == ConsoleKey.S && !ctrl && shift)
{
    var msg = GetSelectedMessage();
    if (msg?.Attachments != null && msg.Attachments.Count > 0)
    {
        SaveAttachmentAs(msg, 0);
        e.Handled = true;
    }
}
else if (e.KeyInfo.Key == ConsoleKey.A && !ctrl && !shift)
{
    var msg = GetSelectedMessage();
    if (msg?.Attachments != null && msg.Attachments.Count > 0)
    {
        SaveAllAttachments(msg);
        e.Handled = true;
    }
}
else if (e.KeyInfo.Key >= ConsoleKey.D1 && e.KeyInfo.Key <= ConsoleKey.D9 && !ctrl)
{
    var msg = GetSelectedMessage();
    var idx = (int)(e.KeyInfo.Key - ConsoleKey.D1);
    if (msg?.Attachments != null && idx < msg.Attachments.Count)
    {
        SaveAttachmentQuick(msg, idx, msg.Attachments[idx].FileName);
        e.Handled = true;
    }
}
```

- [ ] **Step 5: Update ClearReadingPane**

```csharp
public void ClearReadingPane()
{
    if (_readingPane == null) return;
    _readingPane.ClearContents();
    var placeholder = Controls.Markup($"  [{ColorScheme.MutedMarkup}]Select a message to read[/]").Build();
    placeholder.HorizontalAlignment = HorizontalAlignment.Stretch;
    _readingPane.AddControl(placeholder);
}
```

- [ ] **Step 6: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 7: Commit**

```bash
git commit -m "feat: attachment section in message preview with clickable save"
```

---

### Task 5: Compose Dialog — Attachment Support

**Files:**
- Modify: `CXPost/UI/Dialogs/ComposeDialog.cs`

- [ ] **Step 1: Update ComposeResult to include attachments**

```csharp
public record ComposeResult(string To, string? Cc, string Subject, string Body, List<string> AttachmentPaths);
```

- [ ] **Step 2: Add attachment fields and UI to ComposeDialog**

Add fields:
```csharp
private readonly List<string> _attachmentPaths = [];
private ScrollablePanelControl? _attachmentPanel;
private MarkupControl? _attachmentHeader;
```

In `BuildContent`, between the subject field and body editor separator, add:

```csharp
// Attachment section (hidden initially)
_attachmentHeader = Controls.Markup("").Build();
_attachmentHeader.HorizontalAlignment = HorizontalAlignment.Stretch;
_attachmentHeader.Visible = false;
Modal.AddControl(_attachmentHeader);

_attachmentPanel = Controls.ScrollablePanel()
    .WithAlignment(HorizontalAlignment.Stretch)
    .Build();
_attachmentPanel.Visible = false;
Modal.AddControl(_attachmentPanel);
```

- [ ] **Step 3: Add attachment management methods**

```csharp
private void AddAttachment()
{
    _ = Task.Run(async () =>
    {
        var path = await FileDialogs.ShowFilePickerAsync(WindowSystem);
        if (path != null && File.Exists(path))
        {
            _attachmentPaths.Add(path);
            // Update on UI thread
            RefreshAttachmentUI();
        }
    });
}

private void RemoveAttachment(int index)
{
    if (index >= 0 && index < _attachmentPaths.Count)
    {
        _attachmentPaths.RemoveAt(index);
        RefreshAttachmentUI();
    }
}

private void RemoveAllAttachments()
{
    _attachmentPaths.Clear();
    RefreshAttachmentUI();
}

private void RefreshAttachmentUI()
{
    if (_attachmentHeader == null || _attachmentPanel == null) return;

    if (_attachmentPaths.Count == 0)
    {
        _attachmentHeader.Visible = false;
        _attachmentPanel.Visible = false;
        return;
    }

    _attachmentHeader.Visible = true;
    _attachmentHeader.SetContent([$"[{ColorScheme.PrimaryMarkup}]\U0001f4ce Attachments ({_attachmentPaths.Count})[/]"]);

    _attachmentPanel.Visible = true;
    _attachmentPanel.ClearContents();

    for (var i = 0; i < _attachmentPaths.Count; i++)
    {
        var idx = i;
        var fileName = Path.GetFileName(_attachmentPaths[i]);
        var fileSize = new FileInfo(_attachmentPaths[i]).Length;
        var sizeStr = FormatFileSize(fileSize);

        var bar = Controls.StatusBar()
            .AddLeftText($"  {MarkupParser.Escape(fileName)}  [grey50]{sizeStr}[/]")
            .AddRightText($"[{ColorScheme.ErrorMarkup}]\u2715[/]", () => RemoveAttachment(idx))
            .WithMargin(2, 0, 2, 0)
            .Build();
        bar.BackgroundColor = Color.Transparent;
        _attachmentPanel.AddControl(bar);
    }

    // Action bar
    var actionBar = Controls.StatusBar()
        .AddLeft("F2", "Attach", () => AddAttachment())
        .AddLeft("", "Remove All", () => RemoveAllAttachments())
        .WithMargin(2, 0, 2, 0)
        .Build();
    actionBar.BackgroundColor = Color.Transparent;
    _attachmentPanel.AddControl(actionBar);

    Modal.Invalidate(true);
}

private static string FormatFileSize(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
    return $"{bytes / (1024.0 * 1024.0):F1} MB";
}
```

- [ ] **Step 4: Wire F2 key to add attachment**

In `OnKeyPressed`:

```csharp
if (e.KeyInfo.Key == ConsoleKey.F2)
{
    AddAttachment();
    e.Handled = true;
}
```

- [ ] **Step 5: Update TrySend to include attachment paths**

```csharp
CloseWithResult(new ComposeResult(
    To: to,
    Cc: string.IsNullOrWhiteSpace(_ccField?.Input) ? null : _ccField.Input,
    Subject: _subjectField?.Input ?? "",
    Body: _bodyEditor?.Content ?? "",
    AttachmentPaths: new List<string>(_attachmentPaths)));
```

- [ ] **Step 6: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 7: Commit**

```bash
git commit -m "feat: attachment support in compose dialog with F2 file picker"
```

---

### Task 6: ComposeCoordinator — Multipart MIME Send

**Files:**
- Modify: `CXPost/Coordinators/ComposeCoordinator.cs`
- Modify: `CXPost/UI/CXPostApp.cs`

- [ ] **Step 1: Update SendAsync to accept attachment paths and build multipart**

Change `SendAsync` signature to add `List<string>? attachmentPaths = null`:

```csharp
public async Task SendAsync(Account account, string to, string? cc, string subject,
    string body, List<string>? attachmentPaths, CancellationToken ct)
```

Replace the body assignment section:

```csharp
// Build message body
var bodyWithSig = body;
if (!string.IsNullOrEmpty(account.Signature))
    bodyWithSig = body + "\n\n" + account.Signature;

if (attachmentPaths != null && attachmentPaths.Count > 0)
{
    var multipart = new Multipart("mixed");
    multipart.Add(new TextPart("plain") { Text = bodyWithSig });

    foreach (var path in attachmentPaths)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Attachment not found: {path}");

        var mimeType = MimeTypes.GetMimeType(path);
        var attachment = new MimePart(mimeType)
        {
            Content = new MimeContent(File.OpenRead(path)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = Path.GetFileName(path)
        };
        multipart.Add(attachment);
    }
    message.Body = multipart;
}
else
{
    message.Body = new TextPart("plain") { Text = bodyWithSig };
}
```

- [ ] **Step 2: Update all SendAsync call sites in CXPostApp**

All 4 `SendAsync` calls need the new parameter. Change from:

```csharp
await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, _cts.Token);
```

To:

```csharp
await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, result.AttachmentPaths, _cts.Token);
```

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test`

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: multipart MIME send with file attachments"
```

---

### Task 7: Final Integration & Verification

- [ ] **Step 1: Build clean**

Run: `dotnet build`

- [ ] **Step 2: Run all tests**

Run: `dotnet test`

- [ ] **Step 3: Verify attachment column shows in message list**

Check that `FormatMessageRow` returns 5 fields and `PopulateMessageList` creates rows with 5 cells.

- [ ] **Step 4: Final commit if cleanup needed**

```bash
git commit -m "chore: final cleanup for attachments feature"
```
