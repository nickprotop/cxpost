using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

/// <summary>
/// Tabbed settings dialog: Accounts, Appearance, Behavior, About.
/// Returns true if any changes were made (caller should refresh).
/// </summary>
public class SettingsDialog : DialogBase<bool>
{
    private readonly CXPostConfig _config;
    private readonly IConfigService _configService;
    private readonly ICredentialService _credentialService;
    private readonly ICacheService _cacheService;
    private readonly ConsoleWindowSystem _windowSystem;
    private bool _changed;

    // Accounts tab
    private ListControl? _accountList;

    // Appearance tab
    private DropdownControl? _layoutDropdown;

    // Behavior tab
    private PromptControl? _syncIntervalField;
    private CheckboxControl? _notificationsCheckbox;
    private DropdownControl? _startupViewDropdown;
    private CheckboxControl? _confirmQuitCheckbox;

    public SettingsDialog(
        CXPostConfig config,
        IConfigService configService,
        ICredentialService credentialService,
        ICacheService cacheService,
        ConsoleWindowSystem windowSystem)
    {
        _config = config;
        _configService = configService;
        _credentialService = credentialService;
        _cacheService = cacheService;
        _windowSystem = windowSystem;
    }

    protected override string GetTitle() => "Settings";
    protected override (int width, int height) GetSize() => (70, 28);
    protected override bool GetResizable() => true;
    protected override bool GetMaximizable() => true;
    protected override bool GetDefaultResult() => _changed;

    protected override void BuildContent()
    {
        // Header
        Modal.AddControl(Controls.Markup()
            .AddLine("[cyan1 bold]\u2699  Settings[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0).Build());

        // Tab control
        var tabControl = Controls.TabControl()
            .AddTab("Accounts", BuildAccountsTab())
            .AddTab("Appearance", BuildAppearanceTab())
            .AddTab("Behavior", BuildBehaviorTab())
            .AddTab("About", BuildAboutTab())
            .WithActiveTab(0)
            .WithHeaderStyle(TabHeaderStyle.Separator)
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .WithMargin(2, 1, 2, 0)
            .Fill()
            .Build();
        Modal.AddControl(tabControl);

        // Bottom toolbar
        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .StickyBottom().WithMargin(2, 0, 2, 0).Build());

        var toolbar = Controls.Toolbar()
            .AddButton(Controls.Button("[grey93]Edit [cyan1](Enter)[/][/]")
                .WithBackgroundColor(Color.Transparent)
                .WithFocusedBackgroundColor(Color.Grey50)
                .OnClick((s, e) => _ = EditSelectedAsync()).Build())
            .AddButton(Controls.Button("[grey93]Add [cyan1](A)[/][/]")
                .WithBackgroundColor(Color.Transparent)
                .WithFocusedBackgroundColor(Color.DarkGreen)
                .OnClick((s, e) => _ = AddAccountAsync()).Build())
            .AddButton(Controls.Button("[grey93]Delete [cyan1](D)[/][/]")
                .WithBackgroundColor(Color.Transparent)
                .WithFocusedBackgroundColor(Color.DarkRed)
                .OnClick((s, e) => DeleteSelected()).Build())
            .AddSeparator(1)
            .AddButton(Controls.Button("[grey93]Save [cyan1](S)[/][/]")
                .WithBackgroundColor(Color.Transparent)
                .WithFocusedBackgroundColor(Color.DarkGreen)
                .OnClick((s, e) => SaveGlobalSettings()).Build())
            .AddButton(Controls.Button("[grey93]Close [cyan1](Esc)[/][/]")
                .WithBackgroundColor(Color.Transparent)
                .OnClick((s, e) => CloseWithResult(_changed)).Build())
            .WithSpacing(1)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(toolbar);
    }

    // ── Accounts Tab ────────────────────────────────────────────────────

    private ScrollablePanelControl BuildAccountsTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        _accountList = Controls.List()
            .WithAutoHighlightOnFocus(true)
            .WithColors(Color.Grey93, Color.Transparent)
            .WithFocusedColors(Color.Grey93, Color.Transparent)
            .WithHighlightColors(Color.White, Color.Grey30)
            .WithMargin(1, 0, 1, 0)
            .Build();
        _accountList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _accountList.VerticalAlignment = VerticalAlignment.Fill;
        panel.AddControl(_accountList);

        RefreshAccountList();
        return panel;
    }

    // ── Appearance Tab ──────────────────────────────────────────────────

    private ScrollablePanelControl BuildAppearanceTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        panel.AddControl(Controls.Markup("[grey70 bold]Layout[/]")
            .WithMargin(1, 1, 1, 0).Build());

        var layoutOptions = new[] { "Last Used", "Classic (vertical split)", "Wide (3-column)" };
        var currentLayoutIdx = _config.Layout switch
        {
            "last" => 0,
            "classic" => 1,
            "wide" => 2,
            _ => 1
        };
        _layoutDropdown = new DropdownControl("", layoutOptions) { SelectedIndex = currentLayoutIdx };
        _layoutDropdown.Margin = new Margin(1, 0, 1, 1);
        panel.AddControl(_layoutDropdown);

        panel.AddControl(Controls.Markup("[grey50]Classic: folders + messages/preview stacked vertically[/]")
            .WithMargin(1, 0, 1, 0).Build());
        panel.AddControl(Controls.Markup("[grey50]Wide: folders | messages | preview side by side[/]")
            .WithMargin(1, 0, 1, 0).Build());
        panel.AddControl(Controls.Markup("[grey50]Last Used: remembers the layout from F8 toggle[/]")
            .WithMargin(1, 0, 1, 0).Build());

        return panel;
    }

    // ── Behavior Tab ────────────────────────────────────────────────────

    private ScrollablePanelControl BuildBehaviorTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        panel.AddControl(Controls.Markup("[grey70 bold]Synchronization[/]")
            .WithMargin(1, 1, 1, 0).Build());

        panel.AddControl(Controls.Markup($"[{ColorScheme.MutedMarkup}]Default Sync Interval (seconds):[/]")
            .WithMargin(1, 0, 1, 0).Build());
        _syncIntervalField = new PromptControl { Prompt = "", Input = _config.SyncIntervalSeconds.ToString() };
        _syncIntervalField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _syncIntervalField.Margin = new Margin(1, 0, 1, 1);
        panel.AddControl(_syncIntervalField);

        panel.AddControl(Controls.Markup("[grey50]Used for accounts without a per-account interval.[/]")
            .WithMargin(1, 0, 1, 0).Build());

        var behaviorRule = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        behaviorRule.Margin = new Margin(1, 1, 1, 0);
        panel.AddControl(behaviorRule);

        panel.AddControl(Controls.Markup("[grey70 bold]General[/]")
            .WithMargin(1, 1, 1, 0).Build());

        _notificationsCheckbox = Controls.Checkbox("Enable notifications globally")
            .Checked(_config.Notifications)
            .WithMargin(1, 1, 1, 0)
            .Build();
        panel.AddControl(_notificationsCheckbox);

        _confirmQuitCheckbox = Controls.Checkbox("Confirm before quitting")
            .Checked(_config.ConfirmQuit)
            .WithMargin(1, 0, 1, 0)
            .Build();
        panel.AddControl(_confirmQuitCheckbox);

        var startupRule = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        startupRule.Margin = new Margin(1, 1, 1, 0);
        panel.AddControl(startupRule);

        panel.AddControl(Controls.Markup("[grey70 bold]Startup[/]")
            .WithMargin(1, 1, 1, 0).Build());

        var startupOptions = new[] { "All Accounts Dashboard", "Last Used Folder" };
        var currentStartupIdx = _config.StartupView == "last" ? 1 : 0;
        _startupViewDropdown = new DropdownControl("", startupOptions) { SelectedIndex = currentStartupIdx };
        _startupViewDropdown.Margin = new Margin(1, 0, 1, 0);
        panel.AddControl(_startupViewDropdown);

        return panel;
    }

    // ── About Tab ───────────────────────────────────────────────────────

    private ScrollablePanelControl BuildAboutTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        var appVersion = typeof(CXPostConfig).Assembly.GetName().Version?.ToString() ?? "dev";
        var consoleExVersion = typeof(ConsoleWindowSystem).Assembly.GetName().Version?.ToString() ?? "unknown";
        var mailKitVersion = typeof(MailKit.Net.Imap.ImapClient).Assembly.GetName().Version?.ToString() ?? "unknown";

        panel.AddControl(Controls.Markup()
            .AddEmptyLine()
            .AddLine("[cyan1 bold]  CXPost[/]")
            .AddLine($"  [grey70]Cross-platform terminal email client[/]")
            .AddEmptyLine()
            .AddLine($"  [grey50]App Version:[/]        [grey93]{appVersion}[/]")
            .AddLine($"  [grey50]SharpConsoleUI:[/]     [grey93]{consoleExVersion}[/]")
            .AddLine($"  [grey50]MailKit:[/]            [grey93]{mailKitVersion}[/]")
            .AddEmptyLine()
            .AddLine($"  [grey50]Config:[/]  [grey70]{GetConfigPath()}[/]")
            .AddLine($"  [grey50]Data:[/]    [grey70]{GetDataPath()}[/]")
            .AddEmptyLine()
            .AddLine("  [grey35]Built with SharpConsoleUI (ConsoleEx)[/]")
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build());

        return panel;
    }

    private static string GetConfigPath()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CXPost");
    }

    private static string GetDataPath()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CXPost");
    }

    // ── Save Global Settings ────────────────────────────────────────────

    private void SaveGlobalSettings()
    {
        // Layout
        _config.Layout = _layoutDropdown?.SelectedIndex switch
        {
            0 => "last",
            2 => "wide",
            _ => "classic"
        };

        // Behavior
        _config.SyncIntervalSeconds = int.TryParse(_syncIntervalField?.Input, out var si) ? si : 300;
        _config.Notifications = _notificationsCheckbox?.Checked ?? true;
        _config.ConfirmQuit = _confirmQuitCheckbox?.Checked ?? false;
        _config.StartupView = _startupViewDropdown?.SelectedIndex == 1 ? "last" : "dashboard";

        _configService.Save(_config);
        _changed = true;
    }

    // ── Account Management ──────────────────────────────────────────────

    private void RefreshAccountList()
    {
        if (_accountList == null) return;
        _accountList.ClearItems();
        foreach (var account in _config.Accounts)
        {
            var status = string.IsNullOrEmpty(account.ImapHost) ? "[red]not configured[/]" : $"[grey50]{account.ImapHost}[/]";
            _accountList.AddItem($"{account.Name} [grey70]({account.Email})[/]  {status}");
        }
        if (_config.Accounts.Count == 0)
            _accountList.AddItem("[grey50]No accounts configured[/]");
    }

    private async Task EditSelectedAsync()
    {
        var idx = _accountList?.SelectedIndex ?? -1;
        if (idx < 0 || idx >= _config.Accounts.Count) return;

        var existing = _config.Accounts[idx];
        var folders = _cacheService.GetFolders(existing.Id);
        var folderPaths = folders.Select(f => f.Path).OrderBy(p => p).ToList();
        var detectedFolders = folders
            .Where(f => f.FolderType != FolderType.Other)
            .GroupBy(f => f.FolderType)
            .ToDictionary(g => g.Key, g => g.First().Path);
        var dialog = new AccountSettingsDialog(existing, folderPaths, detectedFolders);
        var result = await dialog.ShowAsync(_windowSystem);

        if (result != null)
        {
            _config.Accounts[idx] = result;
            var password = dialog.GetPassword();
            if (!string.IsNullOrEmpty(password))
                _credentialService.StorePassword(result.Id, password);
            _configService.Save(_config);
            _changed = true;
            RefreshAccountList();
        }

        _windowSystem.SetActiveWindow(Modal);
    }

    private async Task AddAccountAsync()
    {
        var dialog = new AccountSettingsDialog();
        var result = await dialog.ShowAsync(_windowSystem);

        if (result != null)
        {
            _config.Accounts.Add(result);
            var password = dialog.GetPassword();
            if (!string.IsNullOrEmpty(password))
                _credentialService.StorePassword(result.Id, password);
            _configService.Save(_config);
            _changed = true;
            RefreshAccountList();
        }

        _windowSystem.SetActiveWindow(Modal);
    }

    private void DeleteSelected()
    {
        var idx = _accountList?.SelectedIndex ?? -1;
        if (idx < 0 || idx >= _config.Accounts.Count) return;

        var account = _config.Accounts[idx];

        _ = Task.Run(async () =>
        {
            var dialog = new ConfirmDialog(
                "Delete Account",
                $"Delete account \"{account.Name}\" ({account.Email})? All cached data will be removed.");
            var confirmed = await dialog.ShowAsync(_windowSystem);
            if (confirmed)
            {
                _credentialService.DeletePassword(account.Id);
                _config.Accounts.RemoveAt(idx);
                _configService.Save(_config);
                _changed = true;
                RefreshAccountList();
            }
            _windowSystem.SetActiveWindow(Modal);
        });
    }

    // ── Key Handling ────────────────────────────────────────────────────

    protected override void SetInitialFocus() => _accountList?.RequestFocus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.Enter:
                _ = EditSelectedAsync();
                e.Handled = true;
                break;
            case ConsoleKey.A:
                _ = AddAccountAsync();
                e.Handled = true;
                break;
            case ConsoleKey.D:
            case ConsoleKey.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
            case ConsoleKey.S:
                SaveGlobalSettings();
                e.Handled = true;
                break;
            default:
                base.OnKeyPressed(sender, e);
                break;
        }
    }
}
