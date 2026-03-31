using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class SearchDialog : DialogBase<string?>
{
    private PromptControl? _searchField;

    protected override string GetTitle() => "Search Messages";
    protected override (int width, int height) GetSize() => (60, 8);
    protected override bool GetResizable() => false;
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var label = Controls.Markup($"[{ColorScheme.PrimaryMarkup}]Search by subject, sender, or body:[/]").Build();
        label.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(label);

        _searchField = new PromptControl { Prompt = "> " };
        _searchField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_searchField);

        var help = Controls.Markup($"[{ColorScheme.MutedMarkup}]Enter: Search  |  Esc: Cancel[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _searchField?.RequestFocus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var query = _searchField?.Input?.Trim();
            if (!string.IsNullOrEmpty(query))
                CloseWithResult(query);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
