# CXPost Roadmap

## Near-term — Polish What Exists

- [x] **Bulk forward dialog** — dedicated compose dialog for forwarding multiple checked messages with shared header text and individual message bodies; includes attachment forwarding for both bulk and single forward
- [x] **Per-folder sync** — Shift+F5 or ↻ Sync button in message list header syncs only the active folder; aggregated views sync that folder type across all accounts sequentially
- [x] **Contact autocomplete** — portal-based autocomplete on To/Cc fields in compose and forward dialogs; triggered by typing 2+ chars, ↓ arrow, or Ctrl+Space; contacts populated from cached mail headers and refreshed on sync
- [ ] **Address book** — browseable contact dialog for searching, browsing, and selecting contacts
- [ ] **Draft save/restore** — Ctrl+S in compose saves to Drafts folder, drafts reopenable from folder tree
- [ ] **Draft save/restore** — Ctrl+S in compose saves to Drafts folder, drafts reopenable from folder tree
- [ ] **Keyboard shortcut overlay (F1)** — quick reference modal showing all shortcuts without leaving the app

## Mid-term — Daily Workflow

- [ ] **Mark spam** — one-key move to Spam folder, similar to Delete → Trash flow
- [ ] **Message snooze** — move to Snoozed folder with timestamp, resurfaces later
- [ ] **Quick reply** — inline reply from preview pane without opening full compose dialog
- [ ] **Export message** — save message as .eml or plain text file to disk

## Longer-term — If Demand Exists

- [ ] **Email rules/filters** — client-side auto-move, auto-read, auto-flag based on sender/subject patterns
- [ ] **Send from alias** — support for Gmail aliases and custom From addresses beyond configured accounts
- [ ] **Message templates** — reusable reply templates stored in config, selectable in compose

## Out of Scope

These are intentionally excluded to keep CXPost focused:

- Calendar/contacts sync (CalDAV/CardDAV)
- PGP/S-MIME encryption
- HTML compose (plain text is the TUI way)
- Plugin system
- Web interface
