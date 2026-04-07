using System.Collections.Concurrent;
using SharpConsoleUI;
using SharpConsoleUI.Animation;
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

public partial class CXPostApp : IDisposable
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
    private System.Threading.Timer? _bodyFetchDebounce;

    // Tracks per-account background sync loops so they can be cancelled and restarted
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _syncLoopCts = new();

    // Aggregated folder lookup for in-place tree updates
    private Dictionary<string, List<MailFolder>> _aggregatedFolders = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<int>? _aggregatedFolderIds;
    private bool _isAggregatedView;

    // Sync animation
    private static readonly string[] SpinnerFrames = ["◐", "◑", "◒", "◓"];
    private int _spinnerIndex;
    private float _syncPulsePhase;

    // Search & filter state
    private volatile bool _isFlaggedFilterActive;
    private volatile bool _isSearchActive;
    public bool IsSearchActive => _isSearchActive;
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
        _currentLayout = _config.Layout == "last"
            ? (_config.LastLayout is "classic" or "wide" ? _config.LastLayout : "classic")
            : (_config.Layout is "classic" or "wide" ? _config.Layout : "classic");
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
            .WithMultiSelect()
            .OnSelectedRowChanged(OnMessageSelected)
            .OnRowActivated(OnMessageActivated)
            .Build();
        _messageTable.CheckboxMode = true;
        _messageTable.FilteringEnabled = true;
        _messageTable.HoverEnabled = false;
        _messageTable.MultiSelectionChanged += (_, count) =>
        {
            UpdateToolbar();
            UpdateHelpBar();
            if (count > 0)
                SetRightPanelHeader($"[grey70]{count} checked[/]", "Clear");
            else
            {
                var msg = GetSelectedMessage();
                if (msg != null) UpdatePreviewHeader(msg);
                else SetRightPanelHeader("[grey70]Messages[/]", showSyncAction: !_isSearchActive);
            }
        };

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
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build();
        _leftPanelHeader.BackgroundColor = ColorScheme.PanelHeaderBackground;

        _rightPanelHeader = Controls.StatusBar()
            .AddLeftText("[grey70]Messages[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();
        _rightPanelHeader.BackgroundColor = ColorScheme.PanelHeaderBackground;

        _previewPanelHeader = Controls.Markup("[grey70]Preview[/]")
            .WithMargin(1, 0, 0, 0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build();
        _previewPanelHeader.BackgroundColor = ColorScheme.PanelHeaderBackground;

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

        // Focus dimming overlays (replaces standalone reading pane fade)
        InitFocusDimming();

        // Confirm before quit
        _mainWindow.OnClosing += (_, args) =>
        {
            if (args.Force || !_config.ConfirmQuit) return;
            args.Allow = false;
            _ = Task.Run(async () =>
            {
                var dialog = new ConfirmDialog("Quit CXPost", "Are you sure you want to quit?");
                var confirmed = await dialog.ShowAsync(_ws);
                if (confirmed)
                    _mainWindow.Close(force: true);
            });
        };

        _ws.AddWindow(_mainWindow);
        _ws.SetActiveWindow(_mainWindow);

        // Populate folder tree with cached data
        PopulateFolderTree();

        // Startup view
        if (_config.StartupView == "last" && !string.IsNullOrEmpty(_config.LastFolderPath))
        {
            // Try to restore last used folder
            var lastFolder = _config.Accounts
                .SelectMany(a => _cacheService.GetFolders(a.Id))
                .FirstOrDefault(f => f.Path == _config.LastFolderPath);
            if (lastFolder != null)
            {
                NavigateToFolder(lastFolder.Id);
            }
            else
            {
                ShowDashboardView(
                    Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService, GetDashboardActions()));
                _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard", onAppClick: NavigateToAllAccounts);
                SetRightPanelHeader("[grey70]Dashboard[/]");
            }
        }
        else
        {
            ShowDashboardView(
                Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService, GetDashboardActions()));
            _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard", onAppClick: NavigateToAllAccounts);
            SetRightPanelHeader("[grey70]Dashboard[/]");
        }

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
            // Import contacts from cached messages in background
            _ = Task.Run(() =>
                ((ContactsService)_contactsService).ImportFromCache(_cacheService, _config.Accounts));

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

                // Advance sync spinner animation + color pulse
                if (_syncCoordinator.SyncingFolderIds.Count > 0)
                {
                    _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
                    _syncPulsePhase = (_syncPulsePhase + 0.15f) % ((float)Math.PI * 2);
                    UpdateSyncSpinner();
                }

                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public void EnqueueUiAction(Action action)
    {
        _pendingUiActions.Enqueue(action);
    }


    public void Dispose()
    {
        StopAllSyncLoops();
        _bodyFetchDebounce?.Dispose();
        try { _bodyFetchCts?.Cancel(); } catch (ObjectDisposedException) { }
        _bodyFetchCts?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }
}
