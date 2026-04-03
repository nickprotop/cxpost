using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class ConfirmDialog : DialogBase<bool>
{
    private readonly string _message;
    private readonly string _title;

    public ConfirmDialog(string title, string message)
    {
        _title = title;
        _message = message;
    }

    protected override string GetTitle() => "";
    protected override (int width, int height) GetSize() => (50, 12);
    protected override bool GetResizable() => false;
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        Modal.AddControl(Controls.Markup()
            .AddLine($"[yellow bold]\u26a0  {_title}[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0).Build());

        Modal.AddControl(Controls.Markup()
            .AddLine($"[grey93]{SharpConsoleUI.Parsing.MarkupParser.Escape(_message)}[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .StickyBottom().WithMargin(2, 0, 2, 0).Build());

        var yesButton = Controls.Button("[grey93]Yes [cyan1](Y)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .WithFocusedBackgroundColor(Color.DarkRed)
            .OnClick((s, e) => CloseWithResult(true))
            .Build();

        var noButton = Controls.Button("[grey93]No [cyan1](N)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .WithFocusedBackgroundColor(Color.Grey30)
            .OnClick((s, e) => CloseWithResult(false))
            .Build();

        var toolbar = Controls.Toolbar()
            .AddButton(yesButton)
            .AddButton(noButton)
            .WithSpacing(2)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(toolbar);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Y)
        {
            CloseWithResult(true);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.N || e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseWithResult(false);
            e.Handled = true;
        }
    }
}
