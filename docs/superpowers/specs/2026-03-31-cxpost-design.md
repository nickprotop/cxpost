# CXPost — TUI Mail Client Design Spec

## Overview

CXPost is a cross-platform terminal mail client built on ConsoleEx (SharpConsoleUI). It targets daily-driver use as a replacement for Evolution/Thunderbird, focusing on core mail workflows: send, receive, reply, search, and organize.

**Name**: CXPost (ConsoleEx + Postal)
**Platform**: .NET 10, cross-platform (Linux, macOS, Windows)
**UI Library**: ConsoleEx (SharpConsoleUI)
**Mail Protocol**: IMAP + SMTP via MailKit

## Architecture

### Pattern: Coordinator + Microsoft DI

Following patterns proven in LazyDotIDE and LazyNuGet, adapted with proper dependency injection via `Microsoft.Extensions.DependencyInjection`.

```csharp
var services = new ServiceCollection();
services.Configure<CXPostConfig>(config);
services.AddSingleton<ConsoleWindowSystem>();
services.AddSingleton<IImapService, ImapService>();
services.AddSingleton<ISmtpService, SmtpService>();
services.AddSingleton<ICacheService, CacheService>();
services.AddSingleton<IContactsService, ContactsService>();
services.AddSingleton<MailSyncCoordinator>();
services.AddSingleton<ComposeCoordinator>();
services.AddSingleton<SearchCoordinator>();
services.AddSingleton<FolderCoordinator>();
services.AddSingleton<CXPostApp>();
var provider = services.BuildServiceProvider();
provider.GetRequiredService<CXPostApp>().Run();
```

### Project Structure

```
CXPost/
├── CXPost/
│   ├── Program.cs                    # Entry point, DI setup
│   ├── Models/
│   │   ├── MailMessage.cs            # Email message (headers, body, flags)
│   │   ├── MailFolder.cs             # IMAP folder (name, path, counts)
│   │   ├── Account.cs                # Account config (server, credentials)
│   │   ├── Contact.cs                # Address book entry
│   │   └── MailThread.cs             # Conversation thread grouping
│   ├── Services/
│   │   ├── IImapService.cs / ImapService.cs       # MailKit IMAP
│   │   ├── ISmtpService.cs / SmtpService.cs       # MailKit SMTP
│   │   ├── ICacheService.cs / CacheService.cs     # SQLite offline cache
│   │   ├── IContactsService.cs / ContactsService.cs # Address autocomplete
│   │   ├── IConfigService.cs / ConfigService.cs   # YAML config
│   │   └── ThreadingService.cs                     # JWZ threading algorithm
│   ├── Coordinators/
│   │   ├── MailSyncCoordinator.cs    # IMAP sync loop, IDLE, new mail
│   │   ├── FolderCoordinator.cs      # Folder tree state, counts
│   │   ├── MessageListCoordinator.cs # Message list, sorting, threading
│   │   ├── ComposeCoordinator.cs     # Compose/reply/forward workflows
│   │   ├── SearchCoordinator.cs      # IMAP SEARCH orchestration
│   │   └── NotificationCoordinator.cs # New mail toasts
│   ├── UI/
│   │   ├── CXPostApp.cs             # Main window, layout, key dispatch
│   │   ├── Panels/
│   │   │   ├── FolderPanel.cs        # TreeControl — folder sidebar
│   │   │   ├── MessageListPanel.cs   # TableControl — message list
│   │   │   └── ReadingPanel.cs       # ScrollablePanel — body + inline compose
│   │   ├── Dialogs/
│   │   │   ├── DialogBase.cs         # Modal base class
│   │   │   ├── ComposeDialog.cs      # New message modal
│   │   │   ├── SettingsDialog.cs     # Account setup, preferences
│   │   │   ├── AccountSetupDialog.cs # Add/edit account
│   │   │   ├── FolderPickerDialog.cs # Move-to-folder picker
│   │   │   └── SearchDialog.cs       # Advanced search
│   │   ├── Components/
│   │   │   ├── StatusBarBuilder.cs   # Top/bottom status bars
│   │   │   ├── MessageFormatter.cs   # Plain text formatting
│   │   │   └── ColorScheme.cs        # Theme colors
│   │   └── KeyBindings.cs           # Central key binding registry
│   └── Data/
│       ├── MailRepository.cs         # SQLite message CRUD
│       ├── ContactRepository.cs      # SQLite contacts CRUD
│       └── DatabaseMigrations.cs     # Schema versioning
└── CXPost.Tests/
    ├── Services/
    ├── Coordinators/
    └── Data/
```

## Layout

### Default: 3-Pane Classic (Layout A)

```
┌──────────────────────────────────────────────────────────────┐
│ CXPost | personal@gmail > Inbox          5 unread | ● Connected │
├───────────────┬──────────────────────────────────────────────┤
│ All Inboxes(12)│ ★  From          Subject              Date  │
│               │ ☆  ● Alice Chen   Re: CXPost arch..   10:32 │
│ PERSONAL@GMAIL│ ★  ● Bob DevOps   Build failed #421   09:15 │
│  📥 Inbox  (5)│ ☆    GitHub       Weekly digest     Yesterday│
│  📤 Sent      │ ☆    NuGet        MailKit 4.12      Yesterday│
│  📝 Drafts (1)├──────────────────────────────────────────────┤
│  🗑 Trash     │ Re: CXPost architecture review               │
│  📁 Projects  │ From: Alice Chen <alice@example.com>         │
│               │ Date: March 31, 2026 10:32 AM                │
│ WORK@COMPANY  │──────────────────────────────────────────────│
│  📥 Inbox  (7)│ Hey,                                         │
│  📤 Sent      │                                              │
│  📁 Team      │ Just reviewed the coordinator architecture.  │
│               │ Looks solid!                                 │
├───────────────┴──────────────────────────────────────────────┤
│ Ctrl+N: Compose │ Ctrl+R: Reply │ Ctrl+S: Search │ Del: Delete│
└──────────────────────────────────────────────────────────────┘
```

### Alternative: 3-Pane Vertical (Layout B, toggle via F8)

Folders left, message list center, reading pane right. All vertical splits.

### ConsoleEx Control Mapping

| UI Component | ConsoleEx Control | Notes |
|---|---|---|
| Folder Panel | TreeControl | Account headers as roots, folders as children, unread badges in markup |
| Message List | TableControl + ITableDataSource | Virtual rendering for 10k+ rows, sortable columns |
| Reading Pane | ScrollablePanelControl + MarkupControl | Swaps to MultilineEditControl on reply |
| Status Bars | TopPanel / BottomPanel | Breadcrumbs, connection status, keybinding hints |
| Folder↔Content split | SplitterControl | Draggable horizontal |
| List↔Reading split | HorizontalSplitterControl | Draggable vertical |

## Compose Workflow

- **Reply / Reply-All / Forward**: Inline — reading pane transforms into MultilineEditControl with quoted text. Headers shown above editor.
- **New message (Ctrl+N)**: Modal window via WindowBuilder.AsModal(). Movable, resizable. Contains To/Cc/Subject prompts + MultilineEditControl body.
- **Pop-out (Ctrl+Shift+E)**: While composing inline, pop out to modal for more space.
- **Send**: Ctrl+Enter. SmtpService → IMAP APPEND to Sent folder → cache update.
- **Save draft**: Ctrl+S while composing. Saved to Drafts folder.
- **Discard**: Esc → confirmation dialog.

## Multi-Account Support

- **Unified Inbox**: Virtual "All Inboxes" node aggregates all account inboxes. Messages sorted by date across accounts.
- **Per-Account Folders**: Each account appears as a root node in the folder tree with its full IMAP folder hierarchy beneath.
- **Account Identification**: From address shown in message list. Status bar shows active account context.
- **Per-Account Signatures**: Text signatures auto-appended on compose, configurable per account.

## Key Bindings

| Action | Shortcut | Context |
|---|---|---|
| Compose new | Ctrl+N | Global → modal |
| Reply | Ctrl+R | Message selected → inline |
| Reply All | Ctrl+Shift+R | Message selected → inline |
| Forward | Ctrl+F | Message selected → inline |
| Search | Ctrl+S | Global → search dialog |
| Delete | Del | Message selected → Trash |
| Move to folder | Ctrl+M | Folder picker dialog |
| Flag/Star | Ctrl+D | Toggle star |
| Mark read/unread | Ctrl+U | Toggle read state |
| Refresh/Sync | F5 | Force sync current folder |
| Settings | Ctrl+, | Settings dialog |
| Switch layout | F8 | Toggle classic ↔ vertical |
| Send (composing) | Ctrl+Enter | Send message |
| Save draft | Ctrl+S | While composing |
| Discard draft | Esc | Confirmation dialog |
| Navigate panes | Tab / Shift+Tab | ConsoleEx focus cycling |

## Data Flow & Sync

```
IMAP Server(s)
    │
ImapService (MailKit)
    │ connect, authenticate, IDLE, fetch, SEARCH
    │
MailSyncCoordinator
    │ per-account sync loop, delta sync via UIDs
    │ IDLE listener for push notifications
    ├──→ CacheService (SQLite)
    │    writes headers, bodies, flags, UID map
    │
    └──→ UI Update Queue (thread-safe Action queue)
         processed by CXPostApp main thread every ~80ms
         → updates FolderPanel, MessageListPanel, ReadingPanel
```

### Sync Strategy

- **Delta sync via UIDs**: Compare local UID list with server on connect. Fetch only new/changed.
- **IDLE for push**: One IDLE connection per account on INBOX. Periodic poll (5 min) for other folders.
- **Offline-first reads**: UI always reads from SQLite cache. Sync writes to cache.
- **Lazy body fetch**: Sync headers first. Fetch body on first read, cache in SQLite.
- **Send flow**: ComposeCoordinator → SmtpService.SendAsync() → IMAP APPEND to Sent → cache update → UI refresh.

## Data Storage

### Locations (OS-standard)

- **Linux**: `~/.config/cxpost/` (config) + `~/.local/share/cxpost/` (data)
- **macOS**: `~/Library/Application Support/CXPost/`
- **Windows**: `%APPDATA%\CXPost\`

Resolved via `Environment.GetFolderPath(SpecialFolder.ApplicationData)` and `SpecialFolder.LocalApplicationData`.

### Config (YAML)

```yaml
accounts:
  - id: "guid-here"
    name: "Personal"
    email: "user@gmail.com"
    imap:
      host: imap.gmail.com
      port: 993
      security: ssl
    smtp:
      host: smtp.gmail.com
      port: 587
      security: starttls
    signature: |
      --
      Sent from CXPost

settings:
  layout: classic          # classic | vertical
  sync_interval_seconds: 300
  notifications: true
```

### SQLite Schema (mail.db)

```sql
CREATE TABLE accounts (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT NOT NULL,
    imap_host TEXT, imap_port INTEGER,
    smtp_host TEXT, smtp_port INTEGER,
    username TEXT,
    signature TEXT,
    last_sync TEXT
);

CREATE TABLE folders (
    id INTEGER PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id),
    path TEXT NOT NULL,
    display_name TEXT NOT NULL,
    uidvalidity INTEGER,
    unread_count INTEGER DEFAULT 0,
    total_count INTEGER DEFAULT 0,
    UNIQUE(account_id, path)
);

CREATE TABLE messages (
    id INTEGER PRIMARY KEY,
    folder_id INTEGER NOT NULL REFERENCES folders(id),
    uid INTEGER NOT NULL,
    message_id TEXT,
    in_reply_to TEXT,
    "references" TEXT,
    thread_id TEXT,
    from_name TEXT, from_address TEXT,
    to_addresses TEXT,
    cc_addresses TEXT,
    subject TEXT,
    date TEXT NOT NULL,
    is_read INTEGER DEFAULT 0,
    is_flagged INTEGER DEFAULT 0,
    has_attachments INTEGER DEFAULT 0,
    body_plain TEXT,
    body_fetched INTEGER DEFAULT 0,
    UNIQUE(folder_id, uid)
);

CREATE INDEX idx_messages_thread ON messages(thread_id);
CREATE INDEX idx_messages_date ON messages(folder_id, date DESC);
CREATE INDEX idx_messages_from ON messages(from_address);
```

### SQLite Schema (contacts.db)

```sql
CREATE TABLE contacts (
    address TEXT PRIMARY KEY,
    display_name TEXT,
    use_count INTEGER DEFAULT 1,
    last_used TEXT
);
```

### Credentials

Passwords and OAuth2 tokens stored in OS keyring — not in SQLite or YAML.
- **Linux**: `secret-tool` CLI (ships with libsecret, avoids P/Invoke)
- **macOS**: `security` CLI (Keychain access)
- **Windows**: `Meziantou.Framework.Win32.CredentialManager` NuGet

**OAuth2 (Gmail, Outlook.com)**: MailKit supports XOAUTH2/OAUTHBEARER SASL. CXPost stores refresh tokens in keyring, exchanges for access tokens on connect. Requires registering a client ID with Google/Microsoft (documented in setup wizard).

## Error Handling

### Connection

- **Startup**: Connect all accounts in parallel. Failed accounts show warning, don't block others. Cached data remains usable.
- **IDLE drop**: Reconnect with exponential backoff (1s, 2s, 4s, max 60s). Detected via 29-min timeout (IMAP spec).
- **Offline mode**: All connections failed → runs from cache. Status bar: `● Offline`. Queued sends saved as drafts.
- **Auth failure**: Notification with "Ctrl+, to update credentials". Account marked degraded.

### Data

- **UIDVALIDITY change**: Purge folder cache, full resync.
- **Concurrent flag changes**: Next sync picks up flag changes via UID FETCH FLAGS.
- **Deleted elsewhere**: EXPUNGE responses remove from cache and UI.

### UI

- **Large folders (10k+)**: ITableDataSource virtual rendering. Sort/filter on SQLite indexes.
- **Slow body fetch**: "Loading..." placeholder with spinner. Cached once fetched.
- **Send failure**: SMTP error notification. Draft preserved. Retry available.

## v1 Feature Scope

### Included
- Send & receive (IMAP + SMTP)
- Reply / Reply-All / Forward
- Folder management (view, move, create/rename/delete)
- Search (server-side IMAP SEARCH)
- Conversation threading (JWZ algorithm on Message-ID/References)
- Address book autocomplete (local, usage-ranked)
- Flags & mark read/unread
- New mail notifications (ConsoleEx toast)
- Offline SQLite cache with lazy body fetch
- Per-account signatures
- Multi-account with unified inbox
- Two layout modes (classic + vertical)

### Deferred (post-v1)
- Attachments (send/receive/save)
- HTML email rendering (strip tags, preserve links)

## Dependencies

| Package | Purpose |
|---|---|
| SharpConsoleUI (ConsoleEx) | Terminal UI framework |
| MailKit + MimeKit | IMAP/SMTP protocol |
| Microsoft.Extensions.DependencyInjection | DI container |
| Microsoft.Extensions.Options | Configuration binding |
| Microsoft.Data.Sqlite | SQLite access |
| YamlDotNet | Config file parsing |
| Libsecret / Keychain bindings | Credential storage |
