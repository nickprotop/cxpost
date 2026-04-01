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
    private record FolderTag(int FolderId);
    private record AccountTag(string AccountId);
    private record AggregatedTag(string TypeKey);

    private readonly ConsoleWindowSystem _ws;
    private readonly IConfigService _configService;
    private readonly ICacheService _cacheService;
    private readonly ICredentialService _credentialService;
    private readonly ImapConnectionFactory _imapFactory;
    private readonly IContactsService _contactsService;
    private readonly MailSyncCoordinator _syncCoordinator;
    private readonly MessageListCoordinator _messageListCoordinator;
    private readonly ComposeCoordinator _composeCoordinator;
    private readonly SearchCoordinator _searchCoordinator;
    private readonly NotificationCoordinator _notificationCoordinator;
    private readonly Components.StatusBarBuilder _statusBar;
    private readonly Components.HelpBar _helpBar;
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
    private ScrollablePanelControl? _dashboardPanel;
    private HorizontalSplitterControl? _listReadingSplitter;

    // Status bar controls
    private MarkupControl? _topStatusRight;
    private MarkupControl? _leftPanelHeader;
    private MarkupControl? _rightPanelHeader;
    private MarkupControl? _previewPanelHeader;

    // Track preview column and its splitter for wide layout visibility
    private ColumnContainer? _previewColumn;
    private SplitterControl? _previewSplitter;

    // Toolbar
    private ToolbarControl? _toolbar;

    // Message bar
    private Components.MessageBar? _messageBar;

    // Aggregated folder lookup for in-place tree updates
    private Dictionary<string, List<MailFolder>> _aggregatedFolders = new(StringComparer.OrdinalIgnoreCase);

    public CXPostApp(
        ConsoleWindowSystem ws,
        IConfigService configService,
        ICacheService cacheService,
        ICredentialService credentialService,
        ImapConnectionFactory imapFactory,
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
        _imapFactory = imapFactory;
        _contactsService = contactsService;
        _syncCoordinator = syncCoordinator;
        _messageListCoordinator = messageListCoordinator;
        _composeCoordinator = composeCoordinator;
        _searchCoordinator = searchCoordinator;
        _notificationCoordinator = notificationCoordinator;
        _statusBar = new Components.StatusBarBuilder();
        _helpBar = new Components.HelpBar(marginLeft: 1);
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
            .WithHighlightColors(Color.White, Color.Grey37)
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
        _listReadingSplitter = Controls.HorizontalSplitter()
            .WithMinHeights(5, 5)
            .Build();

        // Dashboard panel (hidden by default, shown when account/all is selected)
        _dashboardPanel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
        _dashboardPanel.Visible = false;

        // Panel headers
        _leftPanelHeader = Controls.Markup("[grey70]Folders[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        _rightPanelHeader = Controls.Markup("[grey70]Messages[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        _previewPanelHeader = Controls.Markup("[grey70]Preview[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        // Build main grid (layout depends on _currentLayout)
        _mainGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        RebuildMainGrid();

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

        // ── Toolbar ──────────────────────────────────────────────────────────

        _toolbar = Controls.Toolbar()
            .StickyTop()
            .WithSpacing(1)
            .WithWrap()
            .WithMargin(1, 0, 1, 0)
            .WithBackgroundColor(Color.Transparent)
            .WithBelowLineColor(ColorScheme.BorderColor)
            .Build();

        UpdateToolbar();

        // ── Bottom status bar (clickable help bar) ─────────────────────────

        var bottomHelpControl = _statusBar.BottomLeftControl;
        bottomHelpControl.HorizontalAlignment = HorizontalAlignment.Left;
        bottomHelpControl.Margin = new Margin(1, 0, 1, 0);

        // Wire mouse clicks on the bottom bar to the HelpBar
        bottomHelpControl.MouseClick += (_, e) =>
        {
            if (_helpBar.HandleClick(e.Position.X))
                e.Handled = true;
        };

        // Message bar (stacking transient messages)
        _messageBar = new Components.MessageBar();

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
            .HideTitleButtons()
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(Color.Grey27)
            .Maximized()
            .Movable(false)
            .Resizable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithBackgroundGradient(gradient, GradientDirection.Vertical)
            .AddControl(topStatusBar)
            .AddControl(topRule)
            .AddControl(_toolbar)
            .AddControl(_mainGrid)
            .AddControl(_messageBar.Rule)
            .AddControl(_messageBar.Control)
            .AddControl(bottomRule)
            .AddControl(bottomBar)
            .WithAsyncWindowThread(MainLoopAsync)
            .OnKeyPressed(OnKeyPressed)
            .Build();

        _ws.AddWindow(_mainWindow);
        _ws.SetActiveWindow(_mainWindow);

        // Populate folder tree with cached data
        PopulateFolderTree();

        // Show "All Accounts" dashboard on startup
        ShowDashboardView(
            Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService));
        _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard");
        _rightPanelHeader?.SetContent([$"[grey70]Dashboard[/]"]);

        // Update initial status
        _statusBar.UpdateConnectionStatus(0, false);
        UpdateHelpBar();
        UpdateToolbar();

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

    private void RebuildMainGrid()
    {
        if (_mainGrid == null) return;

        // Preserve folder column width across rebuilds
        var columns = _mainGrid.Columns;
        var folderWidth = columns.Count > 0 ? columns[0].Width ?? 28 : 28;

        _mainGrid.ClearColumns();

        // Left column: folder tree (same in both layouts)
        var folderColumn = new ColumnContainer(_mainGrid) { Width = folderWidth };
        folderColumn.AddContent(_leftPanelHeader!);
        folderColumn.AddContent(_folderTree!);
        _mainGrid.AddColumn(folderColumn);

        if (_currentLayout == "wide")
        {
            // Wide layout: Folders | Messages | Preview (3 columns)
            var messageColumn = new ColumnContainer(_mainGrid);
            messageColumn.AddContent(_rightPanelHeader!);
            messageColumn.AddContent(_messageTable!);
            messageColumn.AddContent(_dashboardPanel!);
            _mainGrid.AddColumnWithSplitter(messageColumn);

            _previewColumn = new ColumnContainer(_mainGrid);
            _previewColumn.AddContent(_previewPanelHeader!);
            _previewColumn.AddContent(_readingPane!);
            _previewSplitter = _mainGrid.AddColumnWithSplitter(_previewColumn);

            // Horizontal splitter not used in wide layout
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
        }
        else
        {
            // Classic layout: Folders | Messages / Preview (2 columns, vertical split)
            _previewColumn = null;
            _previewSplitter = null;
            var rightColumn = new ColumnContainer(_mainGrid);
            rightColumn.AddContent(_rightPanelHeader!);
            rightColumn.AddContent(_messageTable!);
            rightColumn.AddContent(_listReadingSplitter!);
            rightColumn.AddContent(_readingPane!);
            rightColumn.AddContent(_dashboardPanel!);
            _mainGrid.AddColumnWithSplitter(rightColumn);

            if (_listReadingSplitter != null) _listReadingSplitter.Visible = true;
        }

        _mainGrid.Invalidate();
    }

    private void UpdateToolbar()
    {
        if (_toolbar == null) return;

        _toolbar.Clear();

        var hasMessage = GetSelectedMessage() != null;
        var isDashboard = _dashboardPanel?.Visible == true;

        // Always available
        AddToolbarButton("\u2709 Compose", () => SimulateKey(ConsoleKey.N, ctrl: true));
        AddToolbarButton("\u21bb Sync", () => SimulateKey(ConsoleKey.F5));
        AddToolbarButton("\u2315 Search", () => SimulateKey(ConsoleKey.S, ctrl: true));

        if (!isDashboard && hasMessage)
        {
            _toolbar.AddItem(new SeparatorControl());
            AddToolbarButton("\u21a9 Reply", () => SimulateKey(ConsoleKey.R, ctrl: true));
            AddToolbarButton("\u21aa Forward", () => SimulateKey(ConsoleKey.F, ctrl: true));
            _toolbar.AddItem(new SeparatorControl());
            AddToolbarButton("\u2691 Flag", () => SimulateKey(ConsoleKey.D, ctrl: true));
            AddToolbarButton("\u2022 Unread", () => SimulateKey(ConsoleKey.U, ctrl: true));
            AddToolbarButton("\u2192 Move", () => SimulateKey(ConsoleKey.M, ctrl: true));
            AddToolbarButton("\u2717 Delete", () => SimulateKey(ConsoleKey.Delete));
        }

        _toolbar.AddItem(new SeparatorControl());
        var layoutLabel = _currentLayout == "classic" ? "\u25eb Wide" : "\u2b12 Classic";
        AddToolbarButton(layoutLabel, () => SimulateKey(ConsoleKey.F8));

        AddToolbarButton("\u2699 Settings", () => SimulateKey(ConsoleKey.OemComma, ctrl: true));
    }

    private void UpdatePreviewHeader(MailMessage? msg = null)
    {
        if (_previewPanelHeader == null) return;

        if (msg != null && _messageTable != null)
        {
            var selectedIdx = _messageTable.SelectedRowIndex + 1;
            var total = _messageTable.RowCount;
            var status = msg.IsRead ? "[grey50]Read[/]" : "[yellow]Unread[/]";
            var date = msg.Date.ToString("MMM d, yyyy 'at' h:mm tt");
            _previewPanelHeader.SetContent(
                [$"[grey70]{selectedIdx} of {total}[/]  {status}  [grey50]{date}[/]"]);
        }
        else
        {
            _previewPanelHeader.SetContent(["[grey70]Preview[/]"]);
        }
    }

    private void AddToolbarButton(string text, Action onClick)
    {
        var button = Controls.Button()
            .WithText(text)
            .WithBorder(ButtonBorderStyle.Rounded)
            .WithBackgroundColor(Color.Transparent)
            .WithBorderBackgroundColor(Color.Transparent)
            .OnClick((_, _) => onClick())
            .Build();
        _toolbar!.AddItem(button);
    }

    private void UpdateHelpBar()
    {
        _helpBar.Clear();

        var hasMessage = GetSelectedMessage() != null;
        var hasFolder = _messageListCoordinator.CurrentFolder != null;

        // Inbox / message list context
        _helpBar.Add("\u2191\u2193", "Navigate");
        _helpBar.Add("Ctrl+N", "Compose", () => SimulateKey(ConsoleKey.N, ctrl: true));

        if (hasMessage)
        {
            _helpBar.Add("Ctrl+R", "Reply", () => SimulateKey(ConsoleKey.R, ctrl: true));
            _helpBar.Add("Ctrl+F", "Forward", () => SimulateKey(ConsoleKey.F, ctrl: true));
            _helpBar.Add("Ctrl+U", "Unread", () => SimulateKey(ConsoleKey.U, ctrl: true));
            _helpBar.Add("Ctrl+D", "Flag", () => SimulateKey(ConsoleKey.D, ctrl: true));
            _helpBar.Add("Del", "Delete", () => SimulateKey(ConsoleKey.Delete));
            _helpBar.Add("Ctrl+M", "Move", () => SimulateKey(ConsoleKey.M, ctrl: true));
        }

        _helpBar.Add("Ctrl+S", "Search", () => SimulateKey(ConsoleKey.S, ctrl: true));
        _helpBar.Add("F5", "Sync", () => SimulateKey(ConsoleKey.F5));
        _helpBar.Add("Ctrl+,", "Settings", () => SimulateKey(ConsoleKey.OemComma, ctrl: true));

        _statusBar.UpdateHelpBar(_helpBar.Render());
    }

    private void SimulateKey(ConsoleKey key, bool ctrl = false, bool shift = false)
    {
        var keyInfo = new ConsoleKeyInfo('\0', key, shift, false, ctrl);
        var args = new KeyPressedEventArgs(keyInfo, false);
        OnKeyPressed(this, args);
    }

    private async Task ShowFirstRunSetupAsync()
    {
        var dialog = new AccountSettingsDialog();
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

                // Periodic re-sync using per-account interval
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

                // Update clock and expire transient messages
                UpdateClockDisplay();
                _messageBar?.Tick();

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

    private static string FormatFolderNodeText(string icon, string displayName, int unread, int total)
    {
        if (unread > 0)
            return $"{icon} {MarkupParser.Escape(displayName)} [yellow]({unread})[/]";
        if (total > 0)
            return $"{icon} {MarkupParser.Escape(displayName)} [grey35]({total})[/]";
        return $"[grey70]{icon} {MarkupParser.Escape(displayName)}[/]";
    }

    private MailFolder? FindFolderById(int folderId)
    {
        foreach (var account in _config.Accounts)
        {
            var folder = _cacheService.GetFolders(account.Id).FirstOrDefault(f => f.Id == folderId);
            if (folder != null) return folder;
        }
        return null;
    }

    public void PopulateFolderTree()
    {
        if (_folderTree == null) return;

        // Gather data
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

        _aggregatedFolders = foldersByType;

        // ── "All Accounts" node ─────────────────────────────────────
        var allNode = _folderTree.FindNodeByTag("all-accounts");
        if (allNode == null)
        {
            allNode = _folderTree.AddRootNode("\U0001f4ec All Accounts");
            allNode.TextColor = ColorScheme.PrimaryText;
            allNode.Tag = "all-accounts";
        }

        // Remove aggregated type children that no longer exist
        foreach (var child in allNode.Children.ToList())
        {
            if (child.Tag is AggregatedTag agg && !foldersByType.ContainsKey(agg.TypeKey))
                allNode.RemoveChild(child);
        }

        // Update or add aggregated type children
        var totalUnread = 0;
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

            totalUnread += unread;

            var text = FormatFolderNodeText(icon, type, unread, total);

            TreeNode? typeNode = null;
            foreach (var child in allNode.Children)
            {
                if (child.Tag is AggregatedTag at && at.TypeKey.Equals(type, StringComparison.OrdinalIgnoreCase))
                { typeNode = child; break; }
            }

            if (typeNode != null)
            {
                typeNode.Text = text;
            }
            else
            {
                var newChild = allNode.AddChild(text);
                newChild.Tag = new AggregatedTag(type);
            }
        }

        // Update "All Accounts" text with total unread
        var allText = totalUnread > 0
            ? $"\U0001f4ec All Accounts [yellow]({totalUnread})[/]"
            : "\U0001f4ec All Accounts";
        allNode.Text = allText;

        // ── Per-account nodes ───────────────────────────────────────
        var currentAccountIds = new HashSet<string>(_config.Accounts.Select(a => a.Id));

        // Remove account root nodes that no longer exist
        foreach (var rootNode in _folderTree.RootNodes.ToList())
        {
            if (rootNode.Tag is AccountTag acctTag && !currentAccountIds.Contains(acctTag.AccountId))
                _folderTree.RemoveRootNode(rootNode);
        }

        foreach (var (account, folders) in allAccountFolders)
        {
            // Find or create account root node
            TreeNode? accountNode = null;
            foreach (var rootNode in _folderTree.RootNodes)
            {
                if (rootNode.Tag is AccountTag acctTag && acctTag.AccountId == account.Id)
                { accountNode = rootNode; break; }
            }

            if (accountNode == null)
            {
                accountNode = _folderTree.AddRootNode($"[grey50 bold]{MarkupParser.Escape(account.Name.ToUpperInvariant())}[/]");
                accountNode.TextColor = ColorScheme.MutedText;
                accountNode.Tag = new AccountTag(account.Id);
            }

            var validFolders = folders
                .Where(f => !(f.DisplayName.StartsWith("[") && f.DisplayName.EndsWith("]")))
                .OrderBy(f => FolderSortKey(f.DisplayName)).ThenBy(f => f.Path)
                .ToList();

            var currentFolderIds = new HashSet<int>(validFolders.Select(f => f.Id));

            // Remove folder children that no longer exist
            foreach (var child in accountNode.Children.ToList())
            {
                if (child.Tag is FolderTag ft && !currentFolderIds.Contains(ft.FolderId))
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
                    if (child.Tag is FolderTag ft && ft.FolderId == folder.Id)
                    { folderNode = child; break; }
                }

                if (folderNode != null)
                {
                    folderNode.Text = text;
                }
                else
                {
                    var newChild = accountNode.AddChild(text);
                    newChild.Tag = new FolderTag(folder.Id);
                }
            }
        }

        _statusBar.UpdateConnectionStatus(totalUnread, _imapFactory.HasAnyConnection);
        _folderTree.Invalidate();
    }

    /// <summary>
    /// Maps folder names from different providers to canonical type names
    /// so they can be aggregated across accounts.
    /// </summary>
    private static string NormalizeFolderType(string displayName, string path)
    {
        var lower = displayName.ToLowerInvariant();
        var pathLower = path.ToLowerInvariant();

        if (lower == "inbox" || pathLower == "inbox") return "Inbox";
        if (lower.Contains("sent") || pathLower.Contains("sent")) return "Sent";
        if (lower.Contains("draft") || pathLower.Contains("draft")) return "Drafts";
        if (lower.Contains("trash") || lower.Contains("deleted") || pathLower.Contains("trash")) return "Trash";
        if (lower.Contains("spam") || lower.Contains("junk") || pathLower.Contains("spam") || pathLower.Contains("junk")) return "Spam";
        if (lower.Contains("archive") || pathLower.Contains("archive") || lower.Contains("all mail")) return "Archive";
        if (lower.Contains("star") || lower.Contains("flagged") || pathLower.Contains("starred")) return "Starred";
        if (lower.Contains("important") || pathLower.Contains("important")) return "Important";
        if (lower.Contains("snoozed") || pathLower.Contains("snoozed")) return "Snoozed";

        return displayName; // Custom folder — keep original name
    }

    private static int FolderSortKey(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("inbox")) return 0;
        if (lower.Contains("sent")) return 1;
        if (lower.Contains("draft")) return 2;
        if (lower.Contains("star") || lower.Contains("flagged")) return 3;
        if (lower.Contains("spam") || lower.Contains("junk")) return 8;
        if (lower.Contains("trash") || lower.Contains("deleted")) return 9;
        return 5;
    }

    private static string GetFolderIcon(string folderName) => MessageFormatter.GetFolderIcon(folderName);

    public void EnqueueUiAction(Action action)
    {
        _pendingUiActions.Enqueue(action);
    }

    private void OnFolderSelected(object? sender, TreeNodeEventArgs args)
    {
        if (args.Node?.Tag is FolderTag ft)
        {
            var folder = FindFolderById(ft.FolderId);
            if (folder == null) return;

            ShowMessageListView();
            _messageListCoordinator.SelectFolder(folder);

            var messages = _cacheService.GetMessages(folder.Id);
            PopulateMessageList(messages);

            var account = _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
            _statusBar.UpdateBreadcrumb(account?.Name ?? "Unknown", folder.DisplayName);
            _rightPanelHeader?.SetContent([$"[grey70]Messages[/] [grey50]({messages.Count})[/]"]);

            ClearReadingPane();
            UpdateHelpBar();
            UpdateToolbar();
        }
        else if (args.Node?.Tag is AggregatedTag agg)
        {
            if (_aggregatedFolders.TryGetValue(agg.TypeKey, out var aggregatedFolders) && aggregatedFolders.Count > 0)
            {
                ShowMessageListView();
                var allMessages = new List<MailMessage>();
                MailFolder? lastFolder = null;
                foreach (var f in aggregatedFolders)
                {
                    allMessages.AddRange(_cacheService.GetMessages(f.Id));
                    lastFolder = f;
                }
                if (lastFolder != null)
                    _messageListCoordinator.SelectFolder(lastFolder);

                allMessages.Sort((a, b) => b.Date.CompareTo(a.Date));
                PopulateMessageList(allMessages);

                _statusBar.UpdateBreadcrumb("All Accounts", agg.TypeKey);
                _rightPanelHeader?.SetContent([$"[grey70]Messages[/] [grey50]({allMessages.Count})[/]"]);

                ClearReadingPane();
                UpdateHelpBar();
                UpdateToolbar();
            }
        }
        else if (args.Node?.Tag is AccountTag acctTag)
        {
            var account = _config.Accounts.FirstOrDefault(a => a.Id == acctTag.AccountId);
            if (account != null)
            {
                ShowDashboardView(
                    Components.AccountDashboard.BuildAccountDashboard(account, _cacheService));

                _statusBar.UpdateBreadcrumb(account.Name, "Dashboard");
                _rightPanelHeader?.SetContent([$"[grey70]Account Dashboard[/]"]);
                UpdateHelpBar();
                UpdateToolbar();
            }
        }
        else if (args.Node?.Tag is string tag && tag == "all-accounts")
        {
            ShowDashboardView(
                Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService));

            _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard");
            _rightPanelHeader?.SetContent([$"[grey70]Dashboard[/]"]);
            UpdateHelpBar();
            UpdateToolbar();
        }
    }

    private void ShowMessageListView()
    {
        // Ensure message list + reading pane are visible, dashboard hidden
        if (_messageTable != null) _messageTable.Visible = true;
        if (_readingPane != null) _readingPane.Visible = true;
        if (_dashboardPanel != null) _dashboardPanel.Visible = false;
        if (_previewPanelHeader != null) _previewPanelHeader.Visible = true;
        if (_previewColumn != null) _previewColumn.Visible = true;
        if (_previewSplitter != null) _previewSplitter.Visible = true;
        UpdatePreviewHeader(GetSelectedMessage());

        if (_currentLayout == "classic")
        {
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = true;
        }
        else
        {
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
        }
    }

    private void ApplyDashboardVisibility()
    {
        if (_messageTable != null) _messageTable.Visible = false;
        if (_readingPane != null) _readingPane.Visible = false;
        if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
        if (_previewPanelHeader != null) _previewPanelHeader.Visible = false;
        if (_previewColumn != null) _previewColumn.Visible = false;
        if (_previewSplitter != null) _previewSplitter.Visible = false;
        if (_dashboardPanel != null) _dashboardPanel.Visible = true;
        UpdatePreviewHeader();
    }

    private void ShowDashboardView(List<IWindowControl> dashboardControls)
    {
        if (_dashboardPanel == null) return;
        _dashboardPanel.ClearContents();
        foreach (var control in dashboardControls)
            _dashboardPanel.AddControl(control);

        ApplyDashboardVisibility();

        // Keep focus on folder tree
        _mainWindow?.FocusManager?.SetFocus(_folderTree as IFocusableControl, FocusReason.Programmatic);
    }

    public void RefreshFolderTree() => PopulateFolderTree();

    public void RefreshCurrentMessageList() => _messageListCoordinator.RefreshMessageList();

    public void ShowError(string message) => _messageBar?.ShowError(message);

    public void ShowSuccess(string message) => _messageBar?.ShowSuccess(message);

    public void ShowInfo(string message) => _messageBar?.ShowInfo(message);

    public void ShowWarning(string message) => _messageBar?.ShowWarning(message);

    public string? ShowPersistent(string message, MessageSeverity severity = MessageSeverity.Info) =>
        _messageBar?.ShowPersistent(message, severity);

    public string? ShowProgress(string message) => _messageBar?.ShowProgress(message);

    public string? ReplaceMessage(string id, string text, MessageSeverity severity = MessageSeverity.Info,
        int? timeoutSeconds = null, bool dismissable = false) =>
        _messageBar?.Replace(id, text, severity, timeoutSeconds, dismissable);

    public void DismissMessage(string id) => _messageBar?.Dismiss(id);

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

        // Remove rows not in incoming (reverse order to keep indices stable)
        var removeIndices = existing
            .Where(kv => !incoming.ContainsKey(kv.Key))
            .Select(kv => kv.Value)
            .OrderByDescending(i => i)
            .ToList();
        foreach (var idx in removeIndices)
            _messageTable.RemoveRow(idx);

        // Rebuild existing lookup after removals (indices shifted)
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

        // If there are new messages, rebuild fully (safe — avoids stale-index issues)
        var hasNewMessages = messages.Any(m => !existing.ContainsKey(m.Uid));
        if (hasNewMessages)
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

    private void OnMessageSelected(object? sender, int rowIndex)
    {
        if (_messageTable == null || rowIndex < 0) return;
        var row = _messageTable.GetRow(rowIndex);
        if (row?.Tag is not MailMessage msg) return;

        // Show what we have immediately (headers + cached body or "Loading...")
        ShowMessagePreview(msg);
        UpdatePreviewHeader(msg);
        UpdateHelpBar();
        UpdateToolbar();

        // Fetch body in background if not cached
        if (!msg.BodyFetched)
        {
            _messageListCoordinator.SelectMessage(msg);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _messageListCoordinator.FetchAndShowBodyAsync(msg, _cts.Token);
                }
                catch (Exception ex)
                {
                    EnqueueUiAction(() => ShowError($"Failed to load message: {ex.Message}"));
                }
            }, _cts.Token);
        }
    }

    private void OnMessageActivated(object? sender, int rowIndex)
    {
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
            $"  [{ColorScheme.MutedMarkup}]To:[/]    {MarkupParser.Escape(MessageFormatter.FormatAddresses(msg.ToAddresses))}",
            ""
        };

        if (msg.BodyFetched && msg.BodyPlain != null)
        {
            // Rule separator between headers and body
            lines.Add($"  [grey23]{"".PadRight(60, '\u2500')}[/]");
            lines.Add("");

            var body = msg.BodyPlain;
            if (MessageFormatter.IsHtml(body))
            {
                // Convert HTML to rich ConsoleEx markup
                var markup = Components.HtmlToMarkup.Convert(body);
                lines.AddRange(markup.Split('\n').Select(l => $"  {l}"));
            }
            else
            {
                // Plain text — escape markup and display
                lines.AddRange(body.Split('\n').Select(l => $"  {MarkupParser.Escape(l)}"));
            }
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

    public void ClearReadingPane()
    {
        _readingContent?.SetContent([$"  [{ColorScheme.MutedMarkup}]Select a message to read[/]"]);
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
                var account = GetCurrentAccount();
                var dialog = new ComposeDialog(_contactsService, cc: account?.DefaultCc ?? "");
                var result = await dialog.ShowAsync(_ws);
                if (result != null)
                {
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
                var (to, subject, body) = _composeCoordinator.PrepareForward(account, msg);
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
                        EnqueueUiAction(() =>
                        {
                            // Select next message or clear reading pane
                            var nextMsg = GetSelectedMessage();
                            if (nextMsg != null)
                                ShowMessagePreview(nextMsg);
                            else
                                ClearReadingPane();
                            UpdateHelpBar();
        UpdateToolbar();
                        });
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
                            var account = GetCurrentAccount();
                            if (account != null)
                            {
                                var imap = _imapFactory.GetConnection(account);
                                var imapLock = _imapFactory.GetLock(account.Id);
                                await imapLock.WaitAsync(_cts.Token);
                                try
                                {
                                    if (!imap.IsConnected)
                                        await imap.ConnectAsync(account, _cts.Token);
                                    await imap.MoveMessageAsync(folder.Path, dest.Path, msg.Uid, _cts.Token);
                                }
                                finally
                                {
                                    imapLock.Release();
                                }
                            }
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
            var isDashboard = _dashboardPanel?.Visible == true;
            _currentLayout = _currentLayout == "classic" ? "wide" : "classic";
            _config.Layout = _currentLayout;
            _configService.Save(_config);
            RebuildMainGrid();

            // Re-apply current view visibility
            if (isDashboard)
                ApplyDashboardVisibility();
            else
                ShowMessageListView();

            UpdateToolbar();
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Settings)
        {
            _ = Task.Run(async () =>
            {
                var dialog = new SettingsDialog(_config, _configService, _credentialService, _ws);
                var changed = await dialog.ShowAsync(_ws);
                if (changed)
                {
                    _config = _configService.Load();

                    // Reset IMAP connections to pick up credential/host changes
                    foreach (var account in _config.Accounts)
                        _ = _imapFactory.ResetConnectionAsync(account.Id);

                    EnqueueUiAction(() =>
                    {
                        RefreshFolderTree();
                        ShowSuccess("Settings saved");
                    });
                }
            });
            e.Handled = true;
        }
    }

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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
