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
    protected override (int width, int height) GetSize() => (60, 12);
    protected override bool GetResizable() => false;
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // Header
        Modal.AddControl(Controls.Markup()
            .AddLine("[cyan1 bold]\U0001f50d  Search Messages[/]")
            .AddLine("[grey70]Search by subject, sender, or body[/]")
            .WithMargin(2, 2, 2, 0)
            .Build());

        // Separator
        var separator = Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build();
        separator.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(separator);

        // Search field
        _searchField = new PromptControl { Prompt = "> " };
        _searchField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _searchField.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(_searchField);

        // Bottom rule
        var bottomRule = Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(Color.Grey23)
            .Build();
        bottomRule.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(bottomRule);

        // Button row
        var searchButton = Controls.Button("[grey93]  Search (Enter)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) =>
            {
                var query = _searchField?.Input?.Trim();
                if (!string.IsNullOrEmpty(query))
                    CloseWithResult(query);
            })
            .Build();

        var cancelButton = Controls.Button("[grey93]  Cancel (Esc)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .OnClick((s, e) => CloseWithResult(null))
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(searchButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(buttonGrid);
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
