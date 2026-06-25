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
    private StatusBarControl? _bottomBar;

    private readonly CancellationTokenSource _cts = new();

    private Window? _mainWindow;
    private CXPostConfig _config;
    private string _currentLayout = "classic";
    private readonly LayoutModeManager _layoutModeManager = new();

    // Panels (created during layout setup)
    private TreeControl? _folderTree;
    private TableControl? _messageTable;
    private ScrollablePanelControl? _readingPane;

    private MarkupControl? _readingContent;
    private HorizontalGridControl? _mainGrid;
    private ScrollablePanelControl? _dashboardPanel;
    private HorizontalSplitterControl? _listReadingSplitter;
    private ListControl? _readModeList;

    // Status bar controls
    private MarkupControl? _topStatusRight;
    private MarkupControl? _leftPanelHeader;
    private StatusBarControl? _rightPanelHeader;
    private StatusBarControl? _previewPanelHeader;

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

    // Conversation threading state
    private bool _isThreadedView;
    private readonly HashSet<string> _expandedThreadIds = new();
    private List<ThreadSummary>? _threadSummaries;
    private string? _readModeThreadContext; // null = show all, non-null = scoped to this threadId

    // Delete flow: prevents re-entry during the 280ms animation window
    private volatile bool _deleteInProgress;

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
        _config = configService.Load();
        _currentLayout = _config.Layout == "last"
            ? (_config.LastLayout is "classic" or "wide" ? _config.LastLayout : "classic")
            : (_config.Layout is "classic" or "wide" ? _config.Layout : "classic");

        // Restore layout widths/heights from config
        if (_config.FolderColumnWidth > 0)
            _layoutModeManager.SaveFolderWidth(_config.FolderColumnWidth);
        if (_config.MessageColumnWidth > 0)
            _layoutModeManager.SaveMessageColumnWidth(_config.MessageColumnWidth);
        if (_config.PreviewColumnWidth > 0)
            _layoutModeManager.SavePreviewColumnWidth(_config.PreviewColumnWidth);
        if (_config.MessageListHeight > 0)
            _layoutModeManager.SaveMessageListHeight(_config.MessageListHeight);
        if (_config.PreviewHidden)
            _layoutModeManager.TogglePreview();
        _isThreadedView = _config.ThreadedView;
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
        _messageTable.TruncationFade = true;
        // Date column (index 4): sort by actual DateTime from row Tag
        var dateCol = _messageTable.Columns[4];
        dateCol.CustomRowComparer = (a, b) =>
        {
            static DateTime ExtractDate(TableRow row) => row.Tag switch
            {
                MailMessage m => m.Date,
                ThreadSummary t => t.NewestMessage.Date,
                _ => DateTime.MinValue
            };
            return ExtractDate(a).CompareTo(ExtractDate(b));
        };
        _messageTable.MultiSelectionChanged += (_, count) =>
        {
            var rowIdx = _messageTable.SelectedRowIndex;
            if (rowIdx >= 0)
                _messageTable.FlashRow(rowIdx, ColorScheme.SelectedRow, TimeSpan.FromMilliseconds(150));
            UpdateToolbar();
            UpdateBottomBar();
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

        // Read mode strip (ListControl replacing crushed table)
        _readModeList = Controls.List()
            .WithHighlightColors(Color.White, Color.Grey37)
            .WithForegroundColor(ColorScheme.SecondaryText)
            .WithBackgroundColor(Color.Transparent)
            .WithFocusedBackgroundColor(Color.Transparent)
            .WithMargin(1, 0, 0, 0)
            .Build();
        _readModeList.TruncationFade = true;
        _readModeList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _readModeList.VerticalAlignment = VerticalAlignment.Fill;

        _readModeList.SelectedItemChanged += (_, item) =>
        {
            if (_isThreadedView && item?.Tag is ThreadSummary selectedThread && selectedThread.IsThread)
            {
                ShowConversationPreview(selectedThread);
                UpdatePreviewHeader(selectedThread.NewestMessage);
                UpdateBottomBar();
                _messageListCoordinator.SelectMessage(selectedThread.NewestMessage);
                DebouncedFetchThreadBodies(selectedThread, selectedThread.NewestMessage);
                return;
            }
            // Unwrap single-message ThreadSummary so it renders as a plain message, not a conversation
            if (item?.Tag is ThreadSummary singleThread && !singleThread.IsThread)
            {
                var only = singleThread.NewestMessage;
                if (_messageTable != null && _readModeList != null)
                {
                    var idx = _readModeList.SelectedIndex;
                    if (idx >= 0 && idx < _messageTable.RowCount)
                        _messageTable.SelectedRowIndex = idx;
                }
                ShowMessagePreview(only);
                UpdatePreviewHeader(only);
                UpdateToolbar();
                UpdateBottomBar();
                return;
            }
            if (_isThreadedView && item?.Tag is MailMessage threadMsg && threadMsg.ThreadId != null)
            {
                var ts = _threadSummaries?.FirstOrDefault(t => t.ThreadId == threadMsg.ThreadId);
                if (ts != null && ts.IsThread)
                {
                    ShowConversationPreview(ts, threadMsg);
                    UpdatePreviewHeader(threadMsg);
                    UpdateBottomBar();
                    _messageListCoordinator.SelectMessage(threadMsg);
                    DebouncedFetchThreadBodies(ts, threadMsg);
                    return;
                }
            }
            if (item?.Tag is MailMessage msg)
            {
                // Sync selection back to message table so GetSelectedMessage() works
                if (_messageTable != null && _readModeList != null)
                {
                    var idx = _readModeList.SelectedIndex;
                    if (idx >= 0 && idx < _messageTable.RowCount)
                        _messageTable.SelectedRowIndex = idx;
                }
                ShowMessagePreview(msg);
                UpdatePreviewHeader(msg);
                UpdateToolbar();
                UpdateBottomBar();
            }
        };

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

        _previewPanelHeader = Controls.StatusBar()
            .AddLeftText("[grey70]Preview[/]")
            .Build();
        _previewPanelHeader.HorizontalAlignment = HorizontalAlignment.Stretch;
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

        // ── Bottom status bar (contextual hints + view toggles) ────────────

        _bottomBar = new StatusBarControl(stickyBottom: false);
        _bottomBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _bottomBar.Margin = new Margin(1, 0, 1, 0);
        _bottomBar.BackgroundColor = Color.Transparent;
        _bottomBar.SeparatorChar = "\u2022";
        _bottomBar.ShortcutLabelSeparator = ":";

        // Message bar (stacking transient messages)
        _messageBar = new Components.MessageBar();

        var bottomRule = Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.BorderColor)
            .Build();

        var bottomBarContainer = Controls.HorizontalGrid()
            .StickyBottom()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col.Add(_bottomBar))
            .Build();
        bottomBarContainer.BackgroundColor = ColorScheme.PanelBackground;
        bottomBarContainer.ForegroundColor = ColorScheme.SecondaryText;

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
            .AddControl(bottomBarContainer)
            .WithAsyncWindowThread(MainLoopAsync)
            .OnKeyPressed(OnKeyPressed)
            .Build();

        // PreviewKeyPressed fires before focused controls consume the key. Used for
        // read-mode arrow interception so arrows browse the message strip even when
        // the strip itself (or any other control) holds focus.
        _mainWindow.PreviewKeyPressed += OnPreviewKeyPressed;


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
        UpdateBottomBar();
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

        var columns = _mainGrid.Columns;
        // Always use saved folder width — the current grid layout may not have folders as column[0]
        var folderWidth = _layoutModeManager.GetSavedFolderWidth();

        _mainGrid.ClearColumns();

        if (_layoutModeManager.IsReadMode)
        {
            // ── Read mode: [folders] | narrow strip | full reader ────────
            if (!_layoutModeManager.IsFolderTreeHidden)
            {
                var folderColumn = new ColumnContainer(_mainGrid) { Width = folderWidth };
                folderColumn.AddContent(_leftPanelHeader!);
                folderColumn.AddContent(_folderTree!);
                _mainGrid.AddColumn(folderColumn);
            }

            // Message strip (narrow) — simple header, no sync/starred buttons
            var stripWidth = _layoutModeManager.IsStripVisible ? LayoutModeManager.StripWidth : 0;
            var stripColumn = new ColumnContainer(_mainGrid) { Width = stripWidth, BackgroundColor = Color.Transparent };
            if (!_layoutModeManager.IsStripVisible)
                stripColumn.Visible = false;
            stripColumn.AddContent(_readModeList!);
            if (!_layoutModeManager.IsFolderTreeHidden)
                _mainGrid.AddColumnWithSplitter(stripColumn);
            else
                _mainGrid.AddColumn(stripColumn);

            // Separator column (thin vertical line, like cxtop dashboard)
            var sepCol = new ColumnContainer(_mainGrid) { Width = 1, BackgroundColor = Color.Transparent };
            sepCol.AddContent(new SeparatorControl
            {
                ForegroundColor = ColorScheme.BorderColor,
                BackgroundColor = Color.Transparent,
                VerticalAlignment = VerticalAlignment.Fill
            });
            _mainGrid.AddColumn(sepCol);

            // Reading pane (fills)
            _previewColumn = new ColumnContainer(_mainGrid);
            _previewColumn.AddContent(_previewPanelHeader!);
            _previewColumn.AddContent(_readingPane!);
            _mainGrid.AddColumn(_previewColumn);

            // Hide horizontal splitter (not used in read mode)
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;

            return;
        }

        // ── Restore table defaults when not in read mode ─────────────────
        _messageTable!.ShowHeader = true;

        // Left column: folder tree (same in both layouts)
        var mainFolderColumn = new ColumnContainer(_mainGrid) { Width = folderWidth };
        mainFolderColumn.AddContent(_leftPanelHeader!);
        mainFolderColumn.AddContent(_folderTree!);
        _mainGrid.AddColumn(mainFolderColumn);

        if (_currentLayout == "wide")
        {
            // Wide layout: Folders | Messages | Preview (3 columns, all with explicit widths)
            // Clear any explicit height from classic layout's horizontal splitter —
            // in wide layout the message table fills its column vertically.
            _messageTable!.Height = null;

            var savedMessageWidth = _layoutModeManager.GetSavedMessageColumnWidth();
            var messageColumn = new ColumnContainer(_mainGrid);
            messageColumn.Width = savedMessageWidth > 0 ? savedMessageWidth : 40;
            messageColumn.AddContent(_rightPanelHeader!);
            messageColumn.AddContent(_messageTable!);
            messageColumn.AddContent(_dashboardPanel!);
            _mainGrid.AddColumnWithSplitter(messageColumn);

            var savedPreviewWidth = _layoutModeManager.GetSavedPreviewColumnWidth();
            _previewColumn = new ColumnContainer(_mainGrid);
            _previewColumn.Width = savedPreviewWidth > 0 ? savedPreviewWidth : 50;
            _previewColumn.AddContent(_previewPanelHeader!);
            _previewColumn.AddContent(_readingPane!);
            _previewSplitter = _mainGrid.AddColumnWithSplitter(_previewColumn);

            // Horizontal splitter not used in wide layout
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;

            // Respect preview hidden state
            if (_layoutModeManager.IsPreviewHidden)
            {
                if (_previewColumn != null) _previewColumn.Width = 0;
            }
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

            // Restore message table height from last session (horizontal splitter position)
            var savedListHeight = _layoutModeManager.GetSavedMessageListHeight();
            if (savedListHeight > 0 && _messageTable != null)
                _messageTable.Height = savedListHeight;

            if (_listReadingSplitter != null) _listReadingSplitter.Visible = true;

            // Respect preview hidden state
            if (_layoutModeManager.IsPreviewHidden)
            {
                if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
                if (_previewPanelHeader != null) _previewPanelHeader.Visible = false;
                if (_readingPane != null) _readingPane.Visible = false;
            }
        }

    }

    private async Task MainLoopAsync(Window window, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // All UI mutations must go through the main thread's queue.
                // This async thread is only a timer — it schedules work, never touches controls directly.
                _ws.EnqueueOnUIThread(() =>
                {
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
                });

                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public void EnqueueUiAction(Action action)
    {
        _ws.EnqueueOnUIThread(action);
    }


    /// <summary>Current row count of the message table, or 0 if not available.</summary>
    public int MessageTableRowCount => _messageTable?.RowCount ?? 0;

    /// <summary>Highlights a row in the message table (used for new message animation).</summary>
    public void HighlightMessageRow(int rowIndex, Color color, TimeSpan duration)
    {
        _messageTable?.HighlightRow(rowIndex, color, duration);
    }

    /// <summary>Pulses a folder tree node by folder ID (used for sync animation).</summary>
    public void PulseFolderNode(int folderId, Color color, int pulseCount, TimeSpan pulseDuration)
    {
        if (_folderTree == null) return;
        var folderNode = _folderTree.FindNodeByTag(new FolderTag(folderId));
        if (folderNode != null)
            _folderTree.PulseNode(folderNode, color, pulseCount, pulseDuration);
    }

    public void Dispose()
    {
        // Best-effort capture of live grid widths. In read mode this is a no-op
        // (grid has a different structure) — safe because EnterReadMode() already
        // captured the normal-mode widths into _layoutModeManager before restructuring.
        // PersistLayoutWidths always writes whatever is in _layoutModeManager to disk.
        SaveCurrentGridWidths();
        PersistLayoutWidths();
        StopAllSyncLoops();
        _bodyFetchDebounce?.Dispose();
        try { _bodyFetchCts?.Cancel(); } catch (ObjectDisposedException) { }
        _bodyFetchCts?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }
}
