using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class SettingsDialog : DialogBase<bool>
{
    private readonly CXPostConfig _config;
    private ListControl? _accountList;

    public SettingsDialog(CXPostConfig config)
    {
        _config = config;
    }

    protected override string GetTitle() => "Settings";
    protected override (int width, int height) GetSize() => (50, 18);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var header = Controls.Markup($"[{ColorScheme.PrimaryMarkup}]Accounts[/]").Build();
        Modal.AddControl(header);

        _accountList = Controls.List()
            .WithAutoHighlightOnFocus(true)
            .Build();

        foreach (var account in _config.Accounts)
            _accountList.AddItem($"{account.Name} ({account.Email})");

        _accountList.AddItem("[+ Add Account]");
        _accountList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _accountList.VerticalAlignment = VerticalAlignment.Fill;
        Modal.AddControl(_accountList);

        var help = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]Enter: Edit  |  Esc: Close[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _accountList?.RequestFocus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            // The caller handles opening AccountSetupDialog based on selection
            CloseWithResult(true);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
