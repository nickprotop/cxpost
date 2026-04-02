# Attachments Feature Design

## Overview

Add full attachment support to CXPost: viewing attachment metadata on received emails, saving attachments to disk, and sending emails with file attachments.

## Data Model

### AttachmentInfo (new class in Models/)

```csharp
public class AttachmentInfo
{
    public string FileName { get; set; }
    public long Size { get; set; }
    public string MimeType { get; set; }
    public int Index { get; set; } // position in MimeMessage for retrieval
}
```

- In-memory only, not persisted to database
- Populated during `FetchBodyAsync` when the full `MimeMessage` is retrieved
- `MailMessage` gets new property: `List<AttachmentInfo>? Attachments { get; set; }`
- Existing `HasAttachments` bool (already in DB) drives the message list indicator

## Reading Attachments

### Message List Column

New column between star and From:

| Star | Clip | From | Subject | Date |
|------|------|------|---------|------|
| ☆ | | john@ | Meeting notes | Mar 28 |
| ★ | 📎 | jane@ | Q4 Report | Mar 27 |

- Column width: 3, center-aligned
- Shows 📎 when `HasAttachments` is true, empty otherwise
- Column indices shift: 0=star, 1=clip, 2=from, 3=subject, 4=date

### Message Preview — Attachment Section

Displayed between headers and body when attachments exist:

```
  📎 Attachments (2)
  ──────────────────────────────────────
  [StatusBarControl: clickable filename]  report.pdf  2.1 MB
  [StatusBarControl: clickable filename]  spreadsheet.xlsx  450 KB
  ──────────────────────────────────────
  [StatusBarControl: S Save | Shift+S Save As | A Save All]
```

- Each attachment is a `StatusBarControl` with the filename as a clickable item — click triggers quick save
- Action bar is a separate `StatusBarControl` with Save/Save As/Save All as clickable items
- Both controls sit inside the reading pane `ScrollablePanelControl`
- Attachment metadata extracted from `MimeMessage` during `FetchBodyAsync`

### Attachment Extraction

`ImapService.FetchBodyAsync` already fetches the full `MimeMessage`. Extend to enumerate attachments:

```csharp
// After fetching MimeMessage:
var attachments = message.Attachments
    .Select((a, i) => new AttachmentInfo
    {
        FileName = a is MimePart mp ? mp.FileName ?? $"attachment_{i}" : $"attachment_{i}",
        Size = a is MimePart mp2 ? mp2.Content?.Stream?.Length ?? 0 : 0,
        MimeType = a.ContentType?.MimeType ?? "application/octet-stream",
        Index = i
    }).ToList();
```

Return attachment list alongside the body text.

### Saving Attachments

New method on `ImapService`:

```csharp
Task SaveAttachmentAsync(string folderPath, uint uid, int attachmentIndex, string targetPath, CancellationToken ct)
```

- Creates ephemeral IMAP connection (same pattern as other user actions)
- Fetches full `MimeMessage`, gets attachment by index, writes to `targetPath`
- Duplicate filename handling: append `(1)`, `(2)` etc. if file exists

Save behaviors:
- **Quick save (S / click filename)**: Save to `~/Downloads/`, show notification
- **Save As (Shift+S)**: Open ConsoleEx built-in folder picker, save to chosen directory
- **Save All (A)**: Save all attachments to `~/Downloads/`, show notification

Keyboard shortcuts active in message preview when attachments exist:
- `S` — save first/only attachment (quick save)
- `Shift+S` — save as (folder picker)
- `A` — save all
- `1-9` — save specific attachment by number

## Sending Attachments

### Compose Dialog Changes

New section between Subject and body editor, hidden when no attachments:

```
📎 Attachments (1)
[StatusBarControl: report.pdf (2.1 MB)  ✕]
[StatusBarControl: F2 Attach | A Remove All]
──────────────────────────────────────
```

- **F2** opens ConsoleEx built-in file picker dialog
- Each file shown as `StatusBarControl` with clickable ✕ to remove
- Action bar: F2 Attach (add more), A Remove All (clear list)
- Files stored as `List<string>` (file paths) until send time

### ComposeCoordinator.SendAsync Changes

When attachments are present, build multipart MIME:

```csharp
if (attachmentPaths.Count > 0)
{
    var multipart = new Multipart("mixed");
    multipart.Add(new TextPart("plain") { Text = bodyWithSig });
    foreach (var path in attachmentPaths)
    {
        var attachment = new MimePart(MimeTypes.GetMimeType(path))
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

### ComposeResult Changes

Extend `ComposeResult` record to include attachment paths:

```csharp
public record ComposeResult(string To, string? Cc, string Subject, string Body, List<string> AttachmentPaths);
```

## Error Handling

- File not found at send time (deleted between attach and send) — show error, don't send
- IMAP fetch failure during save — show error notification
- Disk write failure during save — show error notification
- All save operations are async with notification on success/failure

## Files to Modify

| File | Change |
|------|--------|
| `CXPost/Models/AttachmentInfo.cs` | New file — attachment metadata class |
| `CXPost/Models/MailMessage.cs` | Add `Attachments` property |
| `CXPost/Services/ImapService.cs` | Extract attachment metadata in FetchBodyAsync, add SaveAttachmentAsync |
| `CXPost/Services/IImapService.cs` | Add SaveAttachmentAsync to interface |
| `CXPost/Coordinators/MailSyncCoordinator.cs` | Pass attachment metadata from FetchBodyAsync |
| `CXPost/UI/CXPostApp.cs` | Add clip column, attachment section in preview, save key handlers |
| `CXPost/UI/Dialogs/ComposeDialog.cs` | Add attachment UI (F2, file list, remove) |
| `CXPost/Coordinators/ComposeCoordinator.cs` | Build multipart MIME when attachments present |
| `CXPost/Models/ComposeResult.cs` | Add AttachmentPaths to record |
