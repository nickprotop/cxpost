using System.Collections.Concurrent;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Rendering;
using CXPost.Coordinators;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;
using CXPost.UI.Dialogs;

namespace CXPost.UI;

public class CXPostApp : IDisposable
{
    private readonly ConsoleWindowSystem _ws;
    private readonly IConfigService _configService;
    private readonly ICacheService _cacheService;
    private readonly ICredentialService _credentialService;
    private readonly IImapService _imapService;
    private readonly IContactsService _contactsService;
    private readonly MailSyncCoordinator _syncCoordinator;
    private readonly MessageListCoordinator _messageListCoordinator;
    private readonly ComposeCoordinator _composeCoordinator;
    private readonly SearchCoordinator _searchCoordinator;
    private readonly NotificationCoordinator _notificationCoordinator;
    private readonly Components.StatusBarBuilder _statusBar;
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

    // Status bar controls
    private MarkupControl? _topStatusRight;
    private MarkupControl? _leftPanelHeader;
    private MarkupControl? _rightPanelHeader;

    public CXPostApp(
        ConsoleWindowSystem ws,
        IConfigService configService,
        ICacheService cacheService,
        ICredentialService credentialService,
        IImapService imapService,
        IContactsService contactsService,
        MailSyncCoordinator syncCoordinator,
        MessageListCoordinator messageListCoordinator,
        ComposeCoordinator composeCoordinator,
        SearchCoordinator searchCoordinator,
        NotificationCoordinator notificationCoordinator)
    {
        _ws = ws;
        _configService = configService;
        _cacheService = cacheService;
        _credentialService = credentialService;
        _imapService = imapService;
        _contactsService = contactsService;
        _syncCoordinator = syncCoordinator;
        _messageListCoordinator = messageListCoordinator;
        _composeCoordinator = composeCoordinator;
        _searchCoordinator = searchCoordinator;
        _notificationCoordinator = notificationCoordinator;
        _statusBar = new Components.StatusBarBuilder();
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
        // Folder tree
        _folderTree = Controls.Tree()
            .WithGuide(TreeGuide.Line)
            .WithHighlightColors(Color.White, ColorScheme.SelectedRow)
            .WithBackgroundColor(ColorScheme.SidebarBackground)
            .WithForegroundColor(ColorScheme.SecondaryText)
            .WithMargin(1, 1, 1, 0)
            .Build();

        _folderTree.SelectedNodeChanged += OnFolderSelected;

        // Message table
        _messageTable = Controls.Table()
            .AddColumn("\u2605", TextJustification.Center, width: 3)
            .AddColumn("From", width: 24)
            .AddColumn("Subject")
            .AddColumn("Date", TextJustification.Right, width: 12)
            .WithSorting()
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

        // Panel headers
        _leftPanelHeader = Controls.Markup("[grey70]Folders[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        _rightPanelHeader = Controls.Markup("[grey70]Messages[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        // Build main grid - 2 panels
        _mainGrid = Controls.HorizontalGrid()
            .Column(col => col
                .Width(28)
                .Add(_leftPanelHeader)
                .Add(_folderTree))
            .Column(col =>
            {
                col.Add(_rightPanelHeader);
                col.Add(_messageTable);
                col.Add(listReadingSplitter);
                col.Add(_readingPane);
            })
            .WithSplitterAfter(0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        // Set column background colors
        if (_mainGrid.Columns.Count > 0)
            _mainGrid.Columns[0].BackgroundColor = ColorScheme.SidebarBackground;

        // ── Top status bar ───────────────────────────────────────────────────

        var topStatusLeft = _statusBar.TopLeftControl;
        topStatusLeft.HorizontalAlignment = HorizontalAlignment.Left;
        topStatusLeft.Margin = new Margin(1, 0, 0, 0);

        _topStatusRight = _statusBar.TopRightControl;
        _topStatusRight.HorizontalAlignment = HorizontalAlignment.Right;
        _topStatusRight.Margin = new Margin(0, 0, 1, 0);

        var topStatusBar = Controls.HorizontalGrid()
            .StickyTop()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col.Add(topStatusLeft))
            .Column(col => col.Add(_topStatusRight))
            .Build();
        topStatusBar.BackgroundColor = ColorScheme.PanelBackground;
        topStatusBar.ForegroundColor = Color.Grey93;

        var topRule = Controls.RuleBuilder()
            .StickyTop()
            .WithColor(ColorScheme.BorderColor)
            .Build();

        // ── Bottom status bar ────────────────────────────────────────────────

        var bottomHelpControl = _statusBar.BottomLeftControl;
        bottomHelpControl.HorizontalAlignment = HorizontalAlignment.Left;
        bottomHelpControl.Margin = new Margin(1, 0, 1, 0);

        var bottomRule = Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.BorderColor)
            .Build();

        var bottomBar = Controls.HorizontalGrid()
            .StickyBottom()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col.Add(bottomHelpControl))
            .Build();
        bottomBar.BackgroundColor = ColorScheme.PanelBackground;
        bottomBar.ForegroundColor = ColorScheme.SecondaryText;

        // ── Build window with gradient background ────────────────────────────

        var gradient = ColorGradient.FromColors(new Color(20, 25, 40), new Color(8, 8, 15));

        _mainWindow = new WindowBuilder(_ws)
            .HideTitle()
            .Borderless()
            .Maximized()
            .Movable(false)
            .Resizable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithBackgroundGradient(gradient, GradientDirection.Vertical)
            .AddControl(topStatusBar)
            .AddControl(topRule)
            .AddControl(_mainGrid)
            .AddControl(bottomRule)
            .AddControl(bottomBar)
            .WithAsyncWindowThread(MainLoopAsync)
            .OnKeyPressed(OnKeyPressed)
            .Build();

        _ws.AddWindow(_mainWindow);
        _ws.SetActiveWindow(_mainWindow);

        // Populate folder tree with cached data
        PopulateFolderTree();

        // Update initial status
        _statusBar.UpdateConnectionStatus(0, false);
        UpdateHelpBar();

        // First-run: if no accounts configured, prompt for account setup
        if (_config.Accounts.Count == 0)
        {
            _ = Task.Run(async () =>
            {
                // Small delay to let the window system fully initialize
                await Task.Delay(200, _cts.Token);
                await ShowFirstRunSetupAsync();
            }, _cts.Token);
        }
        else
        {
            // Start background sync for all configured accounts
            StartBackgroundSync();
        }
    }

    private void UpdateHelpBar()
    {
        _statusBar.UpdateHelpBar(
            "[grey50]Ctrl+N[/]: Compose  [grey50]Ctrl+R[/]: Reply  [grey50]Ctrl+S[/]: Search  [grey50]Del[/]: Delete  [grey50]Ctrl+M[/]: Move  [grey50]F5[/]: Sync");
    }

    private async Task ShowFirstRunSetupAsync()
    {
        var dialog = new AccountSetupDialog();
        var account = await dialog.ShowAsync(_ws);
        if (account != null)
        {
            // Store the password
            var password = dialog.GetPassword();
            if (!string.IsNullOrEmpty(password))
                _credentialService.StorePassword(account.Id, password);

            // Save account to config
            _config.Accounts.Add(account);
            _configService.Save(_config);

            // Refresh UI and start sync
            EnqueueUiAction(() =>
            {
                PopulateFolderTree();
                _notificationCoordinator.NotifySendSuccess($"Account {account.Name} added");
            });

            StartBackgroundSync();
        }
    }

    private void StartBackgroundSync()
    {
        foreach (var account in _config.Accounts)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
                    EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                }
                catch (Exception ex)
                {
                    EnqueueUiAction(() =>
                    {
                        _statusBar.UpdateConnectionStatus(0, false);
                        ShowError($"Initial sync failed for {account.Name}: {ex.Message}");
                    });
                }
            }, _cts.Token);
        }
    }

    private int GetTotalUnreadCount()
    {
        var count = 0;
        foreach (var account in _config.Accounts)
        {
            var folders = _cacheService.GetFolders(account.Id);
            count += folders.Sum(f => f.UnreadCount);
        }
        return count;
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

                // Update clock in top-right status bar
                UpdateClockDisplay();

                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void UpdateClockDisplay()
    {
        if (_topStatusRight == null) return;

        var time = DateTime.Now.ToString("h:mm tt");
        var unreadCount = GetTotalUnreadCount();
        var connected = _config.Accounts.Count > 0;

        var status = connected
            ? $"[{ColorScheme.FlaggedMarkup}]{unreadCount} unread[/] [grey50]|[/] [{ColorScheme.SuccessMarkup}]\u25cf Connected[/] [grey50]|[/] [grey70]{time}[/]"
            : $"[{ColorScheme.ErrorMarkup}]\u25cf Offline[/] [grey50]|[/] [grey70]{time}[/]";

        _topStatusRight.SetContent([status]);
    }

    public void PopulateFolderTree()
    {
        if (_folderTree == null) return;
        _folderTree.Clear();

        // "All Inboxes" virtual node
        var allInboxes = _folderTree.AddRootNode("\U0001f4ec All Inboxes");
        allInboxes.TextColor = ColorScheme.PrimaryText;

        foreach (var account in _config.Accounts)
        {
            var accountNode = _folderTree.AddRootNode($"[grey50 bold]{MarkupParser.Escape(account.Name.ToUpperInvariant())}[/]");
            accountNode.TextColor = ColorScheme.MutedText;
            accountNode.Tag = account;

            var folders = _cacheService.GetFolders(account.Id);
            foreach (var folder in folders.OrderBy(f => f.Path))
            {
                var icon = GetFolderIcon(folder.DisplayName);
                var text = folder.UnreadCount > 0
                    ? $"{icon} {MarkupParser.Escape(folder.DisplayName)} [yellow]({folder.UnreadCount})[/]"
                    : $"[grey70]{icon} {MarkupParser.Escape(folder.DisplayName)}[/]";

                var node = accountNode.AddChild(text);
                node.Tag = folder;
            }
        }
    }

    private static string GetFolderIcon(string folderName)
    {
        var lower = folderName.ToLowerInvariant();
        if (lower.Contains("inbox")) return "\U0001f4e5";
        if (lower.Contains("sent")) return "\U0001f4e4";
        if (lower.Contains("draft")) return "\u270f\ufe0f";
        if (lower.Contains("trash") || lower.Contains("deleted")) return "\U0001f5d1\ufe0f";
        if (lower.Contains("spam") || lower.Contains("junk")) return "\u26a0\ufe0f";
        if (lower.Contains("archive")) return "\U0001f4e6";
        if (lower.Contains("star") || lower.Contains("flagged")) return "\u2b50";
        return "\U0001f4c1";
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

            // Update right panel header
            _rightPanelHeader?.SetContent([$"[grey70]Messages[/] [grey50]({messages.Count})[/]"]);
        }
    }

    public void RefreshFolderTree() => PopulateFolderTree();

    public void ShowError(string message)
    {
        _ws.NotificationStateService.ShowNotification(
            "Error", message, SharpConsoleUI.Core.NotificationSeverity.Danger, timeout: 5000);
    }

    public void PopulateMessageList(List<MailMessage> messages)
    {
        if (_messageTable == null) return;

        _messageTable.ClearRows();
        foreach (var msg in messages)
        {
            var star = msg.IsFlagged ? "[yellow]\u2605[/]" : "[grey35]\u2606[/]";
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
        // Full message view -- trigger body fetch if needed
        OnMessageSelected(sender, rowIndex);
    }

    public void ShowMessagePreview(MailMessage msg)
    {
        if (_readingContent == null) return;

        var lines = new List<string>
        {
            "",
            $"  [{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(msg.Subject ?? "(no subject)")}[/]",
            "",
            $"  [{ColorScheme.MutedMarkup}]From:[/]  {MarkupParser.Escape(msg.FromName ?? "")} <{MarkupParser.Escape(msg.FromAddress ?? "")}>",
            $"  [{ColorScheme.MutedMarkup}]Date:[/]  {msg.Date:MMMM d, yyyy h:mm tt}",
            $"  [{ColorScheme.MutedMarkup}]To:[/]    {MarkupParser.Escape(msg.ToAddresses ?? "")}",
            ""
        };

        if (msg.BodyFetched && msg.BodyPlain != null)
        {
            // Rule separator between headers and body
            lines.Add($"  [grey23]{"".PadRight(60, '\u2500')}[/]");
            lines.Add("");
            lines.AddRange(msg.BodyPlain.Split('\n').Select(l => $"  {MarkupParser.Escape(l)}"));
        }
        else
        {
            lines.Add($"  [{ColorScheme.MutedMarkup}]Loading message body...[/]");
        }

        _readingContent.SetContent(lines);

        // Update right header with scroll hint
        if (_readingPane != null && (_readingPane.CanScrollDown || _readingPane.CanScrollUp))
            _rightPanelHeader?.SetContent([$"[grey70]Messages[/] [grey50](\u2191\u2193 to scroll)[/]"]);
    }

    private MailMessage? GetSelectedMessage()
    {
        if (_messageTable == null) return null;
        var idx = _messageTable.SelectedRowIndex;
        if (idx < 0) return null;
        var row = _messageTable.GetRow(idx);
        return row?.Tag as MailMessage;
    }

    private Account? GetCurrentAccount()
    {
        var folder = _messageListCoordinator.CurrentFolder;
        if (folder == null) return _config.Accounts.FirstOrDefault();
        return _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId)
            ?? _config.Accounts.FirstOrDefault();
    }

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift);

        if (ctrl && e.KeyInfo.Key == KeyBindings.ComposeNew)
        {
            _ = Task.Run(async () =>
            {
                var dialog = new ComposeDialog(_contactsService);
                var result = await dialog.ShowAsync(_ws);
                if (result != null)
                {
                    var account = GetCurrentAccount();
                    if (account != null)
                    {
                        try
                        {
                            await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, _cts.Token);
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Send failed: {ex.Message}"));
                        }
                    }
                }
            });
            e.Handled = true;
        }
        else if (ctrl && shift && e.KeyInfo.Key == KeyBindings.Reply)
        {
            // Reply all
            var msg = GetSelectedMessage();
            var account = GetCurrentAccount();
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareReply(account, msg, replyAll: true);
                _ = Task.Run(async () =>
                {
                    var dialog = new ComposeDialog(_contactsService, to, subject, body);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                    {
                        try
                        {
                            await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, _cts.Token);
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Send failed: {ex.Message}"));
                        }
                    }
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Reply)
        {
            var msg = GetSelectedMessage();
            var account = GetCurrentAccount();
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareReply(account, msg, replyAll: false);
                _ = Task.Run(async () =>
                {
                    var dialog = new ComposeDialog(_contactsService, to, subject, body);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                    {
                        try
                        {
                            await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, _cts.Token);
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Send failed: {ex.Message}"));
                        }
                    }
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Forward)
        {
            var msg = GetSelectedMessage();
            var account = GetCurrentAccount();
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareForward(msg);
                _ = Task.Run(async () =>
                {
                    var dialog = new ComposeDialog(_contactsService, to, subject, body);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                    {
                        try
                        {
                            await _composeCoordinator.SendAsync(account, result.To, result.Cc, result.Subject, result.Body, _cts.Token);
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Send failed: {ex.Message}"));
                        }
                    }
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Search)
        {
            _ = Task.Run(async () =>
            {
                var dialog = new SearchDialog();
                var query = await dialog.ShowAsync(_ws);
                if (query != null && _messageListCoordinator.CurrentFolder != null)
                {
                    try
                    {
                        var results = await _searchCoordinator.SearchAsync(
                            _messageListCoordinator.CurrentFolder, query, _cts.Token);
                        EnqueueUiAction(() => PopulateMessageList(results));
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Search failed: {ex.Message}"));
                    }
                }
            });
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Delete)
        {
            var msg = GetSelectedMessage();
            if (msg != null)
            {
                _messageListCoordinator.SelectMessage(msg);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _messageListCoordinator.DeleteMessageAsync(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Delete failed: {ex.Message}"));
                    }
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleFlag)
        {
            var msg = GetSelectedMessage();
            if (msg != null)
            {
                _messageListCoordinator.SelectMessage(msg);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _messageListCoordinator.ToggleFlagAsync(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Toggle flag failed: {ex.Message}"));
                    }
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleRead)
        {
            var msg = GetSelectedMessage();
            if (msg != null)
            {
                _messageListCoordinator.SelectMessage(msg);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _messageListCoordinator.ToggleReadAsync(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Toggle read failed: {ex.Message}"));
                    }
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.MoveToFolder)
        {
            var msg = GetSelectedMessage();
            var folder = _messageListCoordinator.CurrentFolder;
            if (msg != null && folder != null)
            {
                var folders = _cacheService.GetFolders(folder.AccountId);
                _ = Task.Run(async () =>
                {
                    var dialog = new FolderPickerDialog(folders);
                    var dest = await dialog.ShowAsync(_ws);
                    if (dest != null)
                    {
                        try
                        {
                            await _imapService.MoveMessageAsync(folder.Path, dest.Path, msg.Uid, _cts.Token);
                            _cacheService.DeleteMessage(folder.Id, msg.Uid);
                            _messageListCoordinator.RefreshMessageList();
                        }
                        catch (Exception ex)
                        {
                            EnqueueUiAction(() => ShowError($"Move failed: {ex.Message}"));
                        }
                    }
                });
            }
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Refresh)
        {
            _ = Task.Run(async () =>
            {
                foreach (var account in _config.Accounts)
                {
                    try
                    {
                        await _syncCoordinator.SyncAccountAsync(account, _cts.Token);
                        EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Sync failed: {ex.Message}"));
                    }
                }
            });
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.SwitchLayout)
        {
            // Toggle layout (placeholder — layout switching not yet implemented)
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
