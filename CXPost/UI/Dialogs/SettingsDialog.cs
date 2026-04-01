using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

/// <summary>
/// Settings dialog with account management.
/// Returns true if any changes were made (caller should refresh).
/// </summary>
public class SettingsDialog : DialogBase<bool>
{
    private readonly CXPostConfig _config;
    private readonly IConfigService _configService;
    private readonly ICredentialService _credentialService;
    private readonly ConsoleWindowSystem _windowSystem;
    private ListControl? _accountList;
    private bool _changed;

    public SettingsDialog(
        CXPostConfig config,
        IConfigService configService,
        ICredentialService credentialService,
        ConsoleWindowSystem windowSystem)
    {
        _config = config;
        _configService = configService;
        _credentialService = credentialService;
        _windowSystem = windowSystem;
    }

    protected override string GetTitle() => "Settings";
    protected override (int width, int height) GetSize() => (55, 22);
    protected override bool GetDefaultResult() => _changed;

    protected override void BuildContent()
    {
        // Header
        Modal.AddControl(Controls.Markup()
            .AddLine("[cyan1 bold]⚙  Settings[/]")
            .AddLine("[grey70]Manage accounts and preferences[/]")
            .WithMargin(2, 2, 2, 0)
            .Build());

        // Separator
        var separator = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        separator.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(separator);

        // Account list
        _accountList = Controls.List()
            .WithAutoHighlightOnFocus(true)
            .WithMargin(2, 0, 2, 0)
            .Build();
        _accountList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _accountList.VerticalAlignment = VerticalAlignment.Fill;
        Modal.AddControl(_accountList);

        RefreshAccountList();

        // Rule before buttons
        Modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(Color.Grey23)
            .Build());

        // Buttons
        var editButton = Controls.Button("[grey93]  Edit (Enter)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.Grey50)
            .OnClick((s, e) => _ = EditSelectedAsync())
            .Build();

        var addButton = Controls.Button("[grey93]  Add (A)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) => _ = AddAccountAsync())
            .Build();

        var deleteButton = Controls.Button("[grey93]  Delete (D)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.DarkRed)
            .OnClick((s, e) => DeleteSelected())
            .Build();

        var closeButton = Controls.Button("[grey93]  Close (Esc)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .OnClick((s, e) => CloseWithResult(_changed))
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(editButton))
            .Column(col => col.Width(1))
            .Column(col => col.Add(addButton))
            .Column(col => col.Width(1))
            .Column(col => col.Add(deleteButton))
            .Column(col => col.Width(1))
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(buttonGrid);
    }

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
        var dialog = new AccountSettingsDialog(existing);
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

        // Re-focus the settings modal
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
        _credentialService.DeletePassword(account.Id);
        _config.Accounts.RemoveAt(idx);
        _configService.Save(_config);
        _changed = true;
        RefreshAccountList();
    }

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
            default:
                base.OnKeyPressed(sender, e);
                break;
        }
    }
}
