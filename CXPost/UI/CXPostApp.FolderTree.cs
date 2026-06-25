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

public partial class CXPostApp
{
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
                var key = NormalizeFolderType(folder);
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
            ? $"\U0001f4ec All Accounts {FormatUnreadBadge(totalUnread)}"
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
    }

    private void OnFolderSelected(object? sender, TreeNodeEventArgs args)
    {
        // Clear search and filter state when navigating to a different folder
        _isSearchActive = false;
        _activeSearchQuery = null;
        _isFlaggedFilterActive = false;

        if (args.Node?.Tag is FolderTag ft)
        {
            _isAggregatedView = false;
            _aggregatedFolderIds = null;
            var folder = FindFolderById(ft.FolderId);
            if (folder == null) return;

            // Persist last used folder for startup restore
            _config.LastFolderPath = folder.Path;
            _configService.Save(_config);

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
            SetRightPanelHeader($"[grey70]Messages[/] [grey50]({messages.Count})[/]", showSyncAction: true, showFlaggedFilter: true);

            ClearReadingPane();
            UpdateBottomBar();
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
                SetRightPanelHeader($"[grey70]Messages[/] [grey50]({allMessages.Count})[/]", showSyncAction: true, showFlaggedFilter: true);

                ClearReadingPane();
                UpdateBottomBar();
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
                    Components.AccountDashboard.BuildAccountDashboard(account, _cacheService, GetDashboardActions()));

                _statusBar.UpdateBreadcrumb(account.Name, "Dashboard",
                    onAppClick: NavigateToAllAccounts);
                SetRightPanelHeader("[grey70]Account Dashboard[/]");
                UpdateBottomBar();
                UpdateToolbar();
            }
        }
        else if (args.Node?.Tag is string tag && tag == "all-accounts")
        {
            _isAggregatedView = false;
            _aggregatedFolderIds = null;
            ShowDashboardView(
                Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService, GetDashboardActions()));

            _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard",
                onAppClick: NavigateToAllAccounts);
            SetRightPanelHeader("[grey70]Dashboard[/]");
            UpdateBottomBar();
            UpdateToolbar();
        }
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

    /// <summary>
    /// Maps folder names from different providers to canonical type names
    /// so they can be aggregated across accounts.
    /// </summary>
    private static string NormalizeFolderType(MailFolder folder)
    {
        return folder.FolderType switch
        {
            FolderType.Inbox => "Inbox",
            FolderType.Sent => "Sent",
            FolderType.Drafts => "Drafts",
            FolderType.Trash => "Trash",
            FolderType.Spam => "Spam",
            FolderType.Archive => "Archive",
            FolderType.Starred => "Starred",
            FolderType.Important => "Important",
            _ => folder.DisplayName
        };
    }

    private static int FolderSortKey(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower == "inbox") return 0;
        if (lower == "sent") return 1;
        if (lower == "drafts") return 2;
        if (lower == "starred") return 3;
        if (lower == "spam") return 8;
        if (lower == "trash") return 9;
        return 5;
    }

    private static string GetFolderIcon(string folderName) => MessageFormatter.GetFolderIcon(folderName);

    private string FormatFolderNodeText(string icon, string displayName, int unread, int total, bool isSyncing = false)
    {
        var spinner = "";
        if (isSyncing)
        {
            // Smooth color pulse between cyan and steel-blue using sine wave
            var t = (float)(Math.Sin(_syncPulsePhase) * 0.5 + 0.5); // 0..1
            var pulseColor = ColorBlendHelper.BlendColor(
                new Color(70, 130, 180),  // steel blue (dim)
                new Color(0, 255, 255),   // cyan (bright)
                t);
            spinner = $" [rgb({pulseColor.R},{pulseColor.G},{pulseColor.B})]{SpinnerFrames[_spinnerIndex]}[/]";
        }

        if (unread > 0)
            return $"{icon} {MarkupParser.Escape(displayName)} {FormatUnreadBadge(unread)}{spinner}";
        if (total > 0)
            return $"{icon} {MarkupParser.Escape(displayName)} [grey35]({total})[/]{spinner}";
        return $"[grey70]{icon} {MarkupParser.Escape(displayName)}[/]{spinner}";
    }

    /// <summary>
    /// Colors unread count badge on a gradient from muted gold (low) to bright yellow-white (high).
    /// </summary>
    private static string FormatUnreadBadge(int unread)
    {
        // Gradient from dim amber → bright yellow based on count severity
        // 1-2: muted, 3-10: warm, 11-50: bright, 50+: hot
        var t = Math.Clamp(unread / 50f, 0f, 1f);
        var badgeColor = ColorBlendHelper.BlendColor(
            new Color(180, 160, 80),  // muted gold
            new Color(255, 240, 120), // bright warm yellow
            t);
        return $"[rgb({badgeColor.R},{badgeColor.G},{badgeColor.B})]({unread})[/]";
    }

    private void NavigateToFolder(int folderId)
    {
        var folder = FindFolderById(folderId);
        if (folder == null) return;

        // Find and select the folder node in tree
        var folderNode = _folderTree?.FindNodeByTag(new FolderTag(folderId));
        if (folderNode != null && _folderTree != null)
            _folderTree.SelectNode(folderNode);

        // Show messages (same as OnFolderSelected for FolderTag)
        _isSearchActive = false;
        _activeSearchQuery = null;
        _isFlaggedFilterActive = false;
        _isAggregatedView = false;
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
        SetRightPanelHeader($"[grey70]Messages[/] [grey50]({messages.Count})[/]", showSyncAction: true, showFlaggedFilter: true);
        ClearReadingPane();
        UpdateBottomBar();
        UpdateToolbar();
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
            Components.AccountDashboard.BuildAllAccountsDashboard(_config.Accounts, _cacheService, GetDashboardActions()));
        _statusBar.UpdateBreadcrumb("All Accounts", "Dashboard", onAppClick: NavigateToAllAccounts);
        SetRightPanelHeader("[grey70]Dashboard[/]");
        UpdateBottomBar();
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
            Components.AccountDashboard.BuildAccountDashboard(account, _cacheService, GetDashboardActions()));
        _statusBar.UpdateBreadcrumb(account.Name, "Dashboard", onAppClick: NavigateToAllAccounts);
        SetRightPanelHeader("[grey70]Account Dashboard[/]");
        UpdateBottomBar();
        UpdateToolbar();
    }

    public void RefreshFolderTree() => PopulateFolderTree();

    /// <summary>
    /// Called when a folder that was part of the current view is deleted from the server.
    /// </summary>
    public void HandleCurrentFolderDeleted()
    {
        _messageTable?.ClearRows();
        ClearReadingPane();
        UpdatePreviewHeader();
        _messageListCoordinator.SelectFolder(null!);
        UpdateBottomBar();
        UpdateToolbar();
    }
}
