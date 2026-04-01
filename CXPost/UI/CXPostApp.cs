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
    private StatusBarControl? _rightPanelHeader;
    private MarkupControl? _previewPanelHeader;

    // Track preview column and its splitter for wide layout visibility
    private ColumnContainer? _previewColumn;
    private SplitterControl? _previewSplitter;

    // Toolbar
    private ToolbarControl? _toolbar;

    // Message bar
    private Components.MessageBar? _messageBar;

    // Cancels the previous body fetch when user selects a different message
    private CancellationTokenSource? _bodyFetchCts;

    // Tracks per-account background sync loops so they can be cancelled and restarted
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _syncLoopCts = new();

    // Aggregated folder lookup for in-place tree updates
    private Dictionary<string, List<MailFolder>> _aggregatedFolders = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<int>? _aggregatedFolderIds;
    private bool _isAggregatedView;

    // Sync animation
    private static readonly string[] SpinnerFrames = ["◐", "◑", "◒", "◓"];
    private int _spinnerIndex;

    // Search state
    private bool _isSearchActive;
    private string? _activeSearchQuery;
    private readonly List<string> _recentSearches = [];
    private readonly object _searchLock = new();

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
            .AddColumn("\U0001f4ce", TextJustification.Center, width: 4)
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

        _rightPanelHeader = Controls.StatusBar()
            .AddLeftText("[grey70]Messages[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();
        _rightPanelHeader.BackgroundColor = Color.Transparent;

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

        var gradient = ColorGradient.FromColors(new Color(25, 32, 52), new Color(7, 7, 13));

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
        _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard", onAppClick: NavigateToAllAccounts);
        SetRightPanelHeader("[grey70]Dashboard[/]");

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
            rightColumn.AddContent(_previewPanelHeader!);
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
        // Stop any existing sync loops
        StopAllSyncLoops();

        foreach (var account in _config.Accounts)
        {
            var loopCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _syncLoopCts[account.Id] = loopCts;
            var capturedAccount = account; // capture for closure

            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncCoordinator.SyncAccountAsync(capturedAccount, loopCts.Token);
                    EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    EnqueueUiAction(() =>
                    {
                        _statusBar.UpdateConnectionStatus(0, false);
                        ShowError($"Initial sync failed for {capturedAccount.Name}: {ex.Message}");
                    });
                }

                while (!loopCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(capturedAccount.SyncIntervalSeconds), loopCts.Token);
                        await _syncCoordinator.SyncAccountAsync(capturedAccount, loopCts.Token);
                        EnqueueUiAction(() => _statusBar.UpdateConnectionStatus(GetTotalUnreadCount(), true));
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, loopCts.Token);
        }
    }

    private void StopAllSyncLoops()
    {
        foreach (var (id, cts) in _syncLoopCts)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _syncLoopCts.Clear();
    }

    private int GetTotalUnreadCount()
    {
        var count = 0;
        foreach (var account in _config.Accounts)
        {
            var folders = _cacheService.GetFolders(account.Id);
            foreach (var folder in folders)
                count += _cacheService.GetMessages(folder.Id).Count(m => !m.IsRead);
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

                // Advance sync spinner animation
                if (_syncCoordinator.SyncingFolderIds.Count > 0)
                {
                    _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
                    UpdateSyncSpinner();
                }

                await Task.Delay(500, ct);
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

    private void UpdateSyncSpinner()
    {
        if (_folderTree == null) return;
        // Only invalidate the tree to trigger a repaint — the spinner frame
        // is read during render from _spinnerIndex
        _mainWindow?.Invalidate(false);
    }

    private string FormatFolderNodeText(string icon, string displayName, int unread, int total, bool isSyncing = false)
    {
        var spinner = isSyncing ? $" [cyan]{SpinnerFrames[_spinnerIndex]}[/]" : "";
        if (unread > 0)
            return $"{icon} {MarkupParser.Escape(displayName)} [yellow]({unread})[/]{spinner}";
        if (total > 0)
            return $"{icon} {MarkupParser.Escape(displayName)} [grey35]({total})[/]{spinner}";
        return $"[grey70]{icon} {MarkupParser.Escape(displayName)}[/]{spinner}";
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

            var anySyncing = typeFolders.Any(f => _syncCoordinator.SyncingFolderIds.Contains(f.Id));
            var text = FormatFolderNodeText(icon, type, unread, total, anySyncing);

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
                var isSyncing = _syncCoordinator.SyncingFolderIds.Contains(folder.Id);
                var text = FormatFolderNodeText(icon, folder.DisplayName, unread, total, isSyncing);

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
        // Clear search state when navigating to a different folder
        _isSearchActive = false;
        _activeSearchQuery = null;

        if (args.Node?.Tag is FolderTag ft)
        {
            _isAggregatedView = false;
            _aggregatedFolderIds = null;
            var folder = FindFolderById(ft.FolderId);
            if (folder == null) return;

            ShowMessageListView();
            _messageListCoordinator.SelectFolder(folder);

            var messages = _cacheService.GetMessages(folder.Id);
            foreach (var m in messages)
                m.AccountId ??= folder.AccountId;
            PopulateMessageList(messages);

            var account = _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
            _statusBar.UpdateBreadcrumb(account?.Name ?? "Unknown", folder.DisplayName,
                onAppClick: NavigateToAllAccounts,
                onAccountClick: account != null ? () => NavigateToAccount(account.Id) : null);
            SetRightPanelHeader($"[grey70]Messages[/] [grey50]({messages.Count})[/]");

            ClearReadingPane();
            UpdateHelpBar();
            UpdateToolbar();
        }
        else if (args.Node?.Tag is AggregatedTag agg)
        {
            _isAggregatedView = true;
            if (_aggregatedFolders.TryGetValue(agg.TypeKey, out var aggregatedFolders) && aggregatedFolders.Count > 0)
            {
                _aggregatedFolderIds = new HashSet<int>(aggregatedFolders.Select(f => f.Id));
                ShowMessageListView();
                var allMessages = new List<MailMessage>();
                MailFolder? lastFolder = null;
                foreach (var f in aggregatedFolders)
                {
                    var folderMsgs = _cacheService.GetMessages(f.Id);
                    foreach (var m in folderMsgs)
                        m.AccountId ??= f.AccountId;
                    allMessages.AddRange(folderMsgs);
                    lastFolder = f;
                }
                if (lastFolder != null)
                    _messageListCoordinator.SelectFolder(lastFolder);

                allMessages.Sort((a, b) => b.Date.CompareTo(a.Date));
                PopulateMessageList(allMessages);

                _statusBar.UpdateBreadcrumb("All Accounts", agg.TypeKey,
                    onAppClick: NavigateToAllAccounts,
                    onAccountClick: NavigateToAllAccounts);
                SetRightPanelHeader($"[grey70]Messages[/] [grey50]({allMessages.Count})[/]");

                ClearReadingPane();
                UpdateHelpBar();
                UpdateToolbar();
            }
        }
        else if (args.Node?.Tag is AccountTag acctTag)
        {
            _isAggregatedView = false;
            _aggregatedFolderIds = null;
            var account = _config.Accounts.FirstOrDefault(a => a.Id == acctTag.AccountId);
            if (account != null)
            {
                ShowDashboardView(
                    Components.AccountDashboard.BuildAccountDashboard(account, _cacheService));

                _statusBar.UpdateBreadcrumb(account.Name, "Dashboard",
                    onAppClick: NavigateToAllAccounts);
                SetRightPanelHeader("[grey70]Account Dashboard[/]");
                UpdateHelpBar();
                UpdateToolbar();
            }
        }
        else if (args.Node?.Tag is string tag && tag == "all-accounts")
        {
            _isAggregatedView = false;
            _aggregatedFolderIds = null;
            ShowDashboardView(
                Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService));

            _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard",
                onAppClick: NavigateToAllAccounts);
            SetRightPanelHeader("[grey70]Dashboard[/]");
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

    public void RefreshCurrentMessageList()
    {
        if (_isSearchActive) return;
        _messageListCoordinator.RefreshMessageList();
    }

    /// <summary>
    /// Refreshes the message list only if the given folder is currently selected.
    /// </summary>
    public void RefreshCurrentMessageListIfFolder(int folderId)
    {
        if (_isSearchActive) return;

        // Check if this folder is the current folder
        if (_messageListCoordinator.CurrentFolder?.Id == folderId)
        {
            // Check if the currently previewed message was removed
            var selectedMsg = _messageListCoordinator.SelectedMessage;
            if (selectedMsg != null)
            {
                var cachedUids = _cacheService.GetCachedUids(folderId);
                if (!cachedUids.Contains(selectedMsg.Uid))
                {
                    // Selected message was deleted on server — clear preview
                    _messageListCoordinator.SelectMessage(null!);
                    ClearReadingPane();
                    UpdatePreviewHeader();
                }
                else
                {
                    // Refresh the preview in case flags changed
                    var msgs = _cacheService.GetMessages(folderId);
                    var updated = msgs.FirstOrDefault(m => m.Uid == selectedMsg.Uid);
                    if (updated != null && (updated.IsRead != selectedMsg.IsRead || updated.IsFlagged != selectedMsg.IsFlagged))
                    {
                        _messageListCoordinator.SelectMessage(updated);
                        ShowMessagePreview(updated);
                    }
                }
            }

            _messageListCoordinator.RefreshMessageList();
            RetainMessageListFocus();
            return;
        }

        // Check if this folder is part of an aggregated view
        if (_aggregatedFolderIds != null && _aggregatedFolderIds.Contains(folderId))
        {
            RefreshAggregatedView();
            RetainMessageListFocus();
        }
    }

    private void RefreshAggregatedView()
    {
        if (_aggregatedFolderIds == null) return;
        var allMessages = new List<MailMessage>();
        foreach (var fId in _aggregatedFolderIds)
            allMessages.AddRange(_cacheService.GetMessages(fId));
        allMessages.Sort((a, b) => b.Date.CompareTo(a.Date));
        PopulateMessageList(allMessages);
    }

    /// <summary>
    /// Called when a folder that was part of the current view is deleted from the server.
    /// </summary>
    public void HandleCurrentFolderDeleted()
    {
        _messageTable?.ClearRows();
        ClearReadingPane();
        UpdatePreviewHeader();
        _messageListCoordinator.SelectFolder(null!);
        UpdateHelpBar();
        UpdateToolbar();
    }

    private void SetRightPanelHeader(string text, string? clearAction = null)
    {
        if (_rightPanelHeader == null) return;
        _rightPanelHeader.ClearAll();
        _rightPanelHeader.AddLeftText(text);
        if (clearAction != null)
        {
            _rightPanelHeader.AddLeftSeparator();
            _rightPanelHeader.AddLeftText($"[{ColorScheme.PrimaryMarkup}]\u2715 {clearAction}[/]", () => ClearSearch());
        }
    }

    private void NavigateToAllAccounts()
    {
        _isSearchActive = false;
        _activeSearchQuery = null;
        _isAggregatedView = false;

        // Select tree node
        var allNode = _folderTree?.FindNodeByTag("all-accounts");
        if (allNode != null && _folderTree != null)
            _folderTree.SelectNode(allNode);

        // Always show the dashboard (SelectNode may not fire event if already selected)
        ShowDashboardView(
            Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService));
        _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard", onAppClick: NavigateToAllAccounts);
        SetRightPanelHeader("[grey70]Dashboard[/]");
        UpdateHelpBar();
        UpdateToolbar();
    }

    private void NavigateToAccount(string accountId)
    {
        _isSearchActive = false;
        _activeSearchQuery = null;
        _isAggregatedView = false;

        var account = _config.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null) return;

        // Select tree node
        var accountNode = _folderTree?.FindNodeByTag(new AccountTag(accountId));
        if (accountNode != null && _folderTree != null)
            _folderTree.SelectNode(accountNode);

        // Always show the dashboard
        ShowDashboardView(
            Components.AccountDashboard.BuildAccountDashboard(account, _cacheService));
        _statusBar.UpdateBreadcrumb(account.Name, "Dashboard", onAppClick: NavigateToAllAccounts);
        SetRightPanelHeader("[grey70]Account Dashboard[/]");
        UpdateHelpBar();
        UpdateToolbar();
    }

    private void ClearSearch()
    {
        if (!_isSearchActive) return;
        _isSearchActive = false;
        _activeSearchQuery = null;

        // Restore the folder's full message list
        _messageListCoordinator.RefreshMessageList();

        // Restore header
        var folder = _messageListCoordinator.CurrentFolder;
        if (folder != null)
        {
            var messages = _cacheService.GetMessages(folder.Id);
            SetRightPanelHeader($"[grey70]Messages[/] [grey50]({messages.Count})[/]");
        }
        else
        {
            SetRightPanelHeader("[grey70]Messages[/]");
        }

        ClearReadingPane();
        UpdateHelpBar();
        UpdateToolbar();
    }

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

    public void ShowUndoNotification(string id, string text, Action onUndo) =>
        _messageBar?.ShowWithUndo(id, text, onUndo);

    public void PopulateMessageList(List<MailMessage> messages)
    {
        if (_messageTable == null) return;

        // Track if the currently selected message gets removed
        uint? selectedUid = null;
        var selIdx = _messageTable.SelectedRowIndex;
        if (selIdx >= 0 && selIdx < _messageTable.RowCount)
        {
            var selRow = _messageTable.GetRow(selIdx);
            if (selRow.Tag is MailMessage selMsg)
                selectedUid = selMsg.Uid;
        }

        // Build set of incoming UIDs
        var incomingUids = new HashSet<uint>(messages.Select(m => m.Uid));
        var selectedWasRemoved = selectedUid.HasValue && !incomingUids.Contains(selectedUid.Value);

        // Remove rows not in incoming (reverse order to keep indices stable)
        for (var i = _messageTable.RowCount - 1; i >= 0; i--)
        {
            var row = _messageTable.GetRow(i);
            if (row.Tag is MailMessage m && !incomingUids.Contains(m.Uid))
                _messageTable.RemoveRow(i);
        }

        // Build lookup of existing rows by UID → current index
        var existing = new Dictionary<uint, int>();
        for (var i = 0; i < _messageTable.RowCount; i++)
        {
            var row = _messageTable.GetRow(i);
            if (row.Tag is MailMessage m)
                existing[m.Uid] = i;
        }

        // Walk desired order: update existing, insert new
        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            var (star, clip, from, subject, date) = FormatMessageRow(msg);

            if (existing.TryGetValue(msg.Uid, out var rowIdx))
            {
                // Update in-place
                _messageTable.UpdateCell(rowIdx, 0, star);
                _messageTable.UpdateCell(rowIdx, 1, clip);
                _messageTable.UpdateCell(rowIdx, 2, from);
                _messageTable.UpdateCell(rowIdx, 3, subject);
                _messageTable.UpdateCell(rowIdx, 4, date);
                _messageTable.GetRow(rowIdx).Tag = msg;
            }
            else
            {
                // Insert at correct position
                var row = new TableRow(star, clip, from, subject, date) { Tag = msg };
                _messageTable.InsertRow(i, row);

                // Rebuild lookup — indices after insertion shifted
                existing.Clear();
                for (var j = 0; j < _messageTable.RowCount; j++)
                {
                    var r = _messageTable.GetRow(j);
                    if (r.Tag is MailMessage m)
                        existing[m.Uid] = j;
                }
            }
        }

        // If the previously selected message was removed, update the reading pane
        if (selectedWasRemoved)
        {
            var nextMsg = GetSelectedMessage();
            if (nextMsg != null)
            {
                ShowMessagePreview(nextMsg);
                UpdatePreviewHeader(nextMsg);
            }
            else
            {
                ClearReadingPane();
                UpdatePreviewHeader();
            }
            UpdateHelpBar();
            UpdateToolbar();
        }
    }

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
        RetainMessageListFocus();

        _messageListCoordinator.SelectMessage(msg);

        // Cancel any in-flight body fetch from a previous selection
        var oldCts = _bodyFetchCts;
        _bodyFetchCts = null;
        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
        oldCts?.Dispose();

        // Fetch if body not cached, or if attachments expected but not cached yet
        var needsFetch = !msg.BodyFetched || (msg.HasAttachments && msg.Attachments == null);
        if (needsFetch)
        {
            var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _bodyFetchCts = fetchCts;
            var capturedUid = msg.Uid;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _messageListCoordinator.FetchAndShowBodyAsync(msg, fetchCts.Token);

                    // Only update UI if this message is still selected
                    EnqueueUiAction(() =>
                    {
                        if (_messageListCoordinator.SelectedMessage?.Uid == capturedUid)
                        {
                            ShowMessagePreview(msg);
                            RetainMessageListFocus();
                        }
                    });
                }
                catch (OperationCanceledException) { /* superseded by newer selection */ }
                catch (Exception ex)
                {
                    EnqueueUiAction(() => ShowError($"Failed to load message: {ex.Message}"));
                }
            }, fetchCts.Token);
        }
    }

    private void OnMessageActivated(object? sender, int rowIndex)
    {
        OnMessageSelected(sender, rowIndex);
    }

    public void ShowMessagePreview(MailMessage msg)
    {
        if (_readingPane == null) return;

        _readingPane.ClearContents();

        // Header
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

        // Attachment section
        if (msg.Attachments != null && msg.Attachments.Count > 0)
            AddAttachmentControls(msg);
        else if (msg.HasAttachments && msg.Attachments == null)
        {
            var loadingAtt = Controls.Markup(
                $"  [{ColorScheme.MutedMarkup}]\U0001f4ce Loading attachments...[/]").Build();
            _readingPane.AddControl(loadingAtt);
        }

        // Body
        if (msg.BodyFetched && msg.BodyPlain != null)
        {
            var bodyLines = new List<string>
            {
                $"  [grey23]{"".PadRight(60, '\u2500')}[/]",
                ""
            };
            var body = msg.BodyPlain;
            if (MessageFormatter.IsHtml(body))
            {
                var markup = Components.HtmlConverter.ToMarkup(body);
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
            var loading = Controls.Markup($"  [{ColorScheme.MutedMarkup}]Loading message body...[/]").Build();
            _readingPane.AddControl(loading);
        }

        if (!_isSearchActive && (_readingPane.CanScrollDown || _readingPane.CanScrollUp))
            SetRightPanelHeader("[grey70]Messages[/] [grey50](\u2191\u2193 to scroll)[/]");
    }

    private void AddAttachmentControls(MailMessage msg)
    {
        if (_readingPane == null || msg.Attachments == null) return;

        _readingPane.AddControl(Controls.Markup(
            $"  [{ColorScheme.PrimaryMarkup}]\U0001f4ce Attachments ({msg.Attachments.Count})[/]")
            .Build());

        var rule = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
        _readingPane.AddControl(rule);

        foreach (var att in msg.Attachments)
        {
            var sizeStr = FormatFileSize(att.Size);
            var idx = att.Index;
            var fileName = att.FileName;

            var attLabel = Controls.Markup(
                $"  [{ColorScheme.PrimaryMarkup}][[{idx + 1}]][/] {MarkupParser.Escape(fileName)}  [grey50]{sizeStr}[/]")
                .WithMargin(2, 0, 2, 0)
                .Build();
            _readingPane.AddControl(attLabel);

            var attActions = Controls.StatusBar()
                .AddLeft($"{idx + 1}", "Save", () => SaveAttachmentQuick(msg, idx, fileName))
                .AddLeft($"Ctrl+{idx + 1}", "Save As", () => SaveAttachmentAs(msg, idx))
                .WithMargin(4, 0, 2, 0)
                .Build();
            attActions.BackgroundColor = Color.Transparent;
            _readingPane.AddControl(attActions);
        }

        var rule2 = Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(2, 0, 2, 0).Build();
        _readingPane.AddControl(rule2);

        if (msg.Attachments.Count > 1)
        {
            var actionBar = Controls.StatusBar()
                .AddLeft("A", "Save All", () => SaveAllAttachments(msg))
                .AddLeft("Ctrl+A", "Save All to...", () => SaveAllAttachmentsAs(msg))
                .WithMargin(2, 0, 2, 0)
                .Build();
            actionBar.BackgroundColor = Color.Transparent;
            _readingPane.AddControl(actionBar);
        }
    }

    private void SaveAttachmentQuick(MailMessage msg, int index, string fileName)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null) return;

        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var targetPath = Path.Combine(downloadsDir, fileName);
        var msgId = $"save-{msg.Uid}-{index}";

        ReplaceMessage(msgId, $"Saving {fileName}...");

        _ = Task.Run(async () =>
        {
            try
            {
                var imap = _imapFactory.GetFetchConnection(account);
                var fetchLock = _imapFactory.GetFetchLock(account.Id);
                await fetchLock.WaitAsync(_cts.Token);
                try
                {
                    await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                    await imap.SaveAttachmentAsync(folder.Path, msg.Uid, index, targetPath, CancellationToken.None);
                }
                finally { fetchLock.Release(); }
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {fileName} to ~/Downloads/",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private async Task SendWithProgressAsync(Account account, ComposeResult result)
    {
        var sendMsgId = "send-progress";
        try
        {
            EnqueueUiAction(() =>
                ReplaceMessage(sendMsgId, $"Connecting to {account.SmtpHost}..."));

            EnqueueUiAction(() =>
                ReplaceMessage(sendMsgId, $"Sending to {result.To}..."));

            await _composeCoordinator.SendAsync(
                account, result.To, result.Cc, result.Subject,
                result.Body, result.AttachmentPaths, _cts.Token);

            EnqueueUiAction(() =>
            {
                DismissMessage(sendMsgId);
                ShowSuccess($"Message sent to {result.To}");
            });
        }
        catch (Exception ex)
        {
            EnqueueUiAction(() =>
            {
                DismissMessage(sendMsgId);
                ShowError($"Send failed: {ex.Message}");
            });
        }
    }

    private void SaveAttachmentAs(MailMessage msg, int index)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null || msg.Attachments == null) return;

        var fileName = msg.Attachments[index].FileName;

        _ = Task.Run(async () =>
        {
            var dir = await SharpConsoleUI.Dialogs.FileDialogs.ShowFolderPickerAsync(_ws);
            if (dir == null) return;

            var msgId = $"saveas-{msg.Uid}-{index}";
            EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving {fileName}..."));

            var targetPath = Path.Combine(dir, fileName);
            try
            {
                var imap = _imapFactory.GetFetchConnection(account);
                var fetchLock = _imapFactory.GetFetchLock(account.Id);
                await fetchLock.WaitAsync(_cts.Token);
                try
                {
                    await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                    await imap.SaveAttachmentAsync(folder.Path, msg.Uid, index, targetPath, CancellationToken.None);
                }
                finally { fetchLock.Release(); }
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {fileName} to {dir}",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private void SaveAllAttachments(MailMessage msg)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null || msg.Attachments == null) return;

        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var msgId = $"saveall-{msg.Uid}";
        var total = msg.Attachments.Count;

        ReplaceMessage(msgId, $"Saving 1/{total} attachments...");

        _ = Task.Run(async () =>
        {
            try
            {
                var imap = _imapFactory.GetFetchConnection(account);
                var fetchLock = _imapFactory.GetFetchLock(account.Id);
                await fetchLock.WaitAsync(_cts.Token);
                try
                {
                    await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                    for (var i = 0; i < msg.Attachments.Count; i++)
                    {
                        var att = msg.Attachments[i];
                        var progress = i + 1;
                        EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving {progress}/{total}: {att.FileName}..."));
                        var targetPath = Path.Combine(downloadsDir, att.FileName);
                        await imap.SaveAttachmentAsync(folder.Path, msg.Uid, att.Index, targetPath, CancellationToken.None);
                    }
                }
                finally { fetchLock.Release(); }
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {total} attachments to ~/Downloads/",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private void SaveAllAttachmentsAs(MailMessage msg)
    {
        var folder = _messageListCoordinator.CurrentFolder;
        var account = GetCurrentAccount();
        if (folder == null || account == null || msg.Attachments == null) return;

        var total = msg.Attachments.Count;

        _ = Task.Run(async () =>
        {
            var dir = await SharpConsoleUI.Dialogs.FileDialogs.ShowFolderPickerAsync(_ws);
            if (dir == null) return;

            var msgId = $"saveallas-{msg.Uid}";
            EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving 1/{total} attachments..."));

            try
            {
                var imap = _imapFactory.GetFetchConnection(account);
                var fetchLock = _imapFactory.GetFetchLock(account.Id);
                await fetchLock.WaitAsync(_cts.Token);
                try
                {
                    await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                    for (var i = 0; i < msg.Attachments.Count; i++)
                    {
                        var att = msg.Attachments[i];
                        var progress = i + 1;
                        EnqueueUiAction(() => ReplaceMessage(msgId, $"Saving {progress}/{total}: {att.FileName}..."));
                        var targetPath = Path.Combine(dir, att.FileName);
                        await imap.SaveAttachmentAsync(folder.Path, msg.Uid, att.Index, targetPath, CancellationToken.None);
                    }
                }
                finally { fetchLock.Release(); }
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Saved {total} attachments to {dir}",
                    MessageSeverity.Success, timeoutSeconds: 3));
            }
            catch (Exception ex)
            {
                EnqueueUiAction(() => ReplaceMessage(msgId, $"Save failed: {ex.Message}",
                    MessageSeverity.Error, timeoutSeconds: 5));
            }
        }, _cts.Token);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    /// <summary>
    /// Re-focuses the message table if it was the last focused control.
    /// Prevents body fetch / mark-as-read from stealing focus to reading pane.
    /// </summary>
    public void RetainMessageListFocus()
    {
        if (_messageTable == null || _mainWindow == null) return;
        var focused = _mainWindow.FocusManager?.FocusedControl;
        // If nothing is focused, or a non-interactive control got focus, restore to table
        if (focused == null || focused == _readingPane || focused is MarkupControl || focused is ScrollablePanelControl)
            _mainWindow.FocusManager?.SetFocus(_messageTable, FocusReason.Programmatic);
    }

    public void ClearReadingPane()
    {
        if (_readingPane == null) return;
        _readingPane.ClearContents();
        var placeholder = Controls.Markup($"  [{ColorScheme.MutedMarkup}]Select a message to read[/]").Build();
        placeholder.HorizontalAlignment = HorizontalAlignment.Stretch;
        _readingPane.AddControl(placeholder);
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

    /// <summary>
    /// Resolves the account for a specific message — uses the message's AccountId
    /// if available, falls back to folder's AccountId, then first account.
    /// </summary>
    private Account? GetAccountForMessage(MailMessage? msg)
    {
        if (msg?.AccountId != null)
            return _config.Accounts.FirstOrDefault(a => a.Id == msg.AccountId);

        // Fallback: try folder
        var folder = _messageListCoordinator.CurrentFolder;
        if (folder != null)
            return _config.Accounts.FirstOrDefault(a => a.Id == folder.AccountId);

        return _config.Accounts.FirstOrDefault();
    }

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift);

        if (e.KeyInfo.Key == ConsoleKey.Escape && _isSearchActive)
        {
            ClearSearch();
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ComposeNew)
        {
            _ = Task.Run(async () =>
            {
                var account = GetAccountForMessage(null);
                var fromDisplay = account != null ? $"{account.Name} <{account.Email}>" : "";
                var dialog = new ComposeDialog(_contactsService, fromDisplay, cc: account?.DefaultCc ?? "");
                var result = await dialog.ShowAsync(_ws);
                if (result != null && account != null)
                    await SendWithProgressAsync(account, result);
            });
            e.Handled = true;
        }
        else if (ctrl && shift && e.KeyInfo.Key == KeyBindings.Reply)
        {
            // Reply all
            var msg = GetSelectedMessage();
            var account = GetAccountForMessage(msg);
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareReply(account, msg, replyAll: true);
                _ = Task.Run(async () =>
                {
                    var fromDisplay = $"{account.Name} <{account.Email}>";
                    var dialog = new ComposeDialog(_contactsService, fromDisplay, to: to, subject: subject, body: body);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                        await SendWithProgressAsync(account, result);
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Reply)
        {
            var msg = GetSelectedMessage();
            var account = GetAccountForMessage(msg);
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareReply(account, msg, replyAll: false);
                _ = Task.Run(async () =>
                {
                    var fromDisplay = $"{account.Name} <{account.Email}>";
                    var dialog = new ComposeDialog(_contactsService, fromDisplay, to: to, subject: subject, body: body);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                        await SendWithProgressAsync(account, result);
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Forward)
        {
            var msg = GetSelectedMessage();
            var account = GetAccountForMessage(msg);
            if (msg != null && account != null)
            {
                var (to, subject, body) = _composeCoordinator.PrepareForward(account, msg);
                _ = Task.Run(async () =>
                {
                    var fromDisplay = $"{account.Name} <{account.Email}>";
                    var dialog = new ComposeDialog(_contactsService, fromDisplay, to: to, subject: subject, body: body);
                    var result = await dialog.ShowAsync(_ws);
                    if (result != null)
                        await SendWithProgressAsync(account, result);
                });
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.Search)
        {
            _ = Task.Run(async () =>
            {
                var dialog = new SearchDialog(_recentSearches);
                var query = await dialog.ShowAsync(_ws);
                if (query != null)
                {
                    // Track recent searches
                    lock (_searchLock)
                    {
                        _recentSearches.Remove(query);
                        _recentSearches.Insert(0, query);
                        if (_recentSearches.Count > 5) _recentSearches.RemoveAt(5);
                    }

                    _isSearchActive = true;
                    _activeSearchQuery = query;

                    // Determine search folders
                    var searchFolders = new List<MailFolder>();
                    if (_isAggregatedView && _aggregatedFolders.Count > 0)
                    {
                        foreach (var folders in _aggregatedFolders.Values)
                            searchFolders.AddRange(folders);
                    }
                    else if (_messageListCoordinator.CurrentFolder != null)
                    {
                        searchFolders.Add(_messageListCoordinator.CurrentFolder);
                    }

                    if (searchFolders.Count == 0) return;

                    // Local search — instant results from cache
                    var localResults = new List<MailMessage>();
                    var lowerQuery = query.ToLowerInvariant();
                    foreach (var folder in searchFolders)
                    {
                        var msgs = _cacheService.GetMessages(folder.Id);
                        localResults.AddRange(msgs.Where(m =>
                            (m.Subject?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (m.FromName?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (m.FromAddress?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false)));
                    }
                    localResults.Sort((a, b) => b.Date.CompareTo(a.Date));

                    var folderName = searchFolders.Count == 1
                        ? searchFolders[0].DisplayName
                        : "All Folders";

                    EnqueueUiAction(() =>
                    {
                        PopulateMessageList(localResults);
                        SetRightPanelHeader(
                            $"[grey70]Search:[/] [white]{MarkupParser.Escape(query)}[/] [grey50]({localResults.Count} results in {MarkupParser.Escape(folderName)})[/]",
                            "Clear");
                        ShowInfo($"Searching server for \"{query}\"...");
                    });

                    // Server search — refine with IMAP results
                    try
                    {
                        var serverResults = new List<MailMessage>();
                        foreach (var folder in searchFolders)
                        {
                            var results = await _searchCoordinator.SearchAsync(folder, query, _cts.Token);
                            serverResults.AddRange(results);
                        }
                        serverResults.Sort((a, b) => b.Date.CompareTo(a.Date));

                        if (_isSearchActive && _activeSearchQuery == query)
                        {
                            EnqueueUiAction(() =>
                            {
                                PopulateMessageList(serverResults);
                                SetRightPanelHeader(
                                    $"[grey70]Search:[/] [white]{MarkupParser.Escape(query)}[/] [grey50]({serverResults.Count} results in {MarkupParser.Escape(folderName)})[/]",
                                    "Clear");
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        EnqueueUiAction(() => ShowError($"Server search failed: {ex.Message}"));
                    }
                }
            });
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == KeyBindings.Delete)
        {
            var msg = GetSelectedMessage();
            var folder = _messageListCoordinator.CurrentFolder;
            if (msg != null && folder != null)
            {
                // Optimistic delete: instant UI removal + undo notification + deferred IMAP
                _messageListCoordinator.DeleteMessageOptimistic(msg, folder, _cts.Token);

                // Update UI after removal
                var nextMsg = GetSelectedMessage();
                if (nextMsg != null)
                    ShowMessagePreview(nextMsg);
                else
                    ClearReadingPane();
                UpdateHelpBar();
                UpdateToolbar();
            }
            e.Handled = true;
        }
        else if (ctrl && e.KeyInfo.Key == KeyBindings.ToggleFlag)
        {
            var msg = GetSelectedMessage();
            var folder = _messageListCoordinator.CurrentFolder;
            if (msg != null && folder != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _messageListCoordinator.ToggleFlagAsync(msg, folder, _cts.Token);
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
            var folder = _messageListCoordinator.CurrentFolder;
            if (msg != null && folder != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _messageListCoordinator.ToggleReadAsync(msg, folder, _cts.Token);
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
                            var account = GetAccountForMessage(msg);
                            if (account != null)
                            {
                                var imap = _imapFactory.GetFetchConnection(account);
                                var fetchLock = _imapFactory.GetFetchLock(account.Id);
                                await fetchLock.WaitAsync(_cts.Token);
                                try
                                {
                                    await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
                                    await imap.MoveMessageAsync(folder.Path, dest.Path, msg.Uid, CancellationToken.None);
                                }
                                finally { fetchLock.Release(); }
                            }
                            EnqueueUiAction(() =>
                            {
                                _cacheService.DeleteMessage(folder.Id, msg.Uid);
                                _messageListCoordinator.RefreshMessageList();
                                ClearReadingPane();
                                UpdatePreviewHeader();
                                UpdateHelpBar();
                                UpdateToolbar();
                                ShowSuccess($"Moved to {dest.DisplayName}");
                            });
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
        else if (e.KeyInfo.Key == ConsoleKey.A && !ctrl && !shift)
        {
            var msg = GetSelectedMessage();
            if (msg?.Attachments != null && msg.Attachments.Count > 1)
            {
                SaveAllAttachments(msg);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == ConsoleKey.A && ctrl && !shift)
        {
            var msg = GetSelectedMessage();
            if (msg?.Attachments != null && msg.Attachments.Count > 1)
            {
                SaveAllAttachmentsAs(msg);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key >= ConsoleKey.D1 && e.KeyInfo.Key <= ConsoleKey.D9)
        {
            var msg = GetSelectedMessage();
            var idx = (int)(e.KeyInfo.Key - ConsoleKey.D1);
            if (msg?.Attachments != null && idx < msg.Attachments.Count)
            {
                if (ctrl)
                    SaveAttachmentAs(msg, idx);
                else
                    SaveAttachmentQuick(msg, idx, msg.Attachments[idx].FileName);
                e.Handled = true;
            }
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

                    // Restart sync loops with updated accounts
                    StartBackgroundSync();

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
        StopAllSyncLoops();
        try { _bodyFetchCts?.Cancel(); } catch (ObjectDisposedException) { }
        _bodyFetchCts?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }
}
