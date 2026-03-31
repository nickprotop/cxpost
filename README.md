# CXPost

A cross-platform TUI mail client built with [ConsoleEx](https://github.com/nprotopapas/ConsoleEx) (SharpConsoleUI).

CXPost brings a polished terminal UI to email with IMAP/SMTP support, offline caching, conversation threading, and multi-account unified inbox.

## Features

- **IMAP + SMTP** via MailKit with IDLE push notifications
- **Multi-account** with unified inbox and per-account folder trees
- **Offline cache** with SQLite-backed message storage and lazy body fetch
- **Conversation threading** using JWZ algorithm (Message-ID/References)
- **3-pane layout** (classic or vertical) with draggable splitters
- **Compose** inline replies or modal new messages
- **Search** via server-side IMAP SEARCH
- **Address autocomplete** from local contact history
- **Per-account signatures**
- **Background gradient**, rich markup, clickable status bars
- **Cross-platform** credential storage (secret-tool, Keychain, Credential Manager)

## Requirements

- .NET 10
- A terminal with Unicode support

## Build & Run

```bash
dotnet build
dotnet run --project CXPost
```

On first run, CXPost will prompt you to set up an email account.

## License

MIT
