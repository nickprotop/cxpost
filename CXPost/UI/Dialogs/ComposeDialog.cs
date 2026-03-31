using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public record ComposeResult(string To, string? Cc, string Subject, string Body);

public class ComposeDialog : DialogBase<ComposeResult?>
{
    private readonly IContactsService _contacts;
    private readonly string _initialTo;
    private readonly string _initialSubject;
    private readonly string _initialBody;

    private PromptControl? _toField;
    private PromptControl? _ccField;
    private PromptControl? _subjectField;
    private MultilineEditControl? _bodyEditor;

    public ComposeDialog(
        IContactsService contacts,
        string to = "",
        string subject = "",
        string body = "")
    {
        _contacts = contacts;
        _initialTo = to;
        _initialSubject = subject;
        _initialBody = body;
    }

    protected override string GetTitle() => "New Message";
    protected override (int width, int height) GetSize() => (80, 28);
    protected override bool GetResizable() => true;
    protected override ComposeResult? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // To field
        var toLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]To:[/]").Build();
        toLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(toLabel);

        _toField = new PromptControl { Prompt = "", Input = _initialTo };
        _toField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_toField);

        // Cc field
        var ccLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Cc:[/]").Build();
        ccLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(ccLabel);

        _ccField = new PromptControl { Prompt = "", Input = "" };
        _ccField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_ccField);

        // Subject field
        var subjectLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Subject:[/]").Build();
        subjectLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(subjectLabel);

        _subjectField = new PromptControl { Prompt = "", Input = _initialSubject };
        _subjectField.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(_subjectField);

        // Separator
        var rule = Controls.Markup($"[{ColorScheme.MutedMarkup}]{"─".PadRight(76, '─')}[/]").Build();
        Modal.AddControl(rule);

        // Body editor
        _bodyEditor = Controls.MultilineEdit(_initialBody)
            .WithWrapMode(WrapMode.WrapWords)
            .WithPlaceholder("Type your message here...")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .IsEditing()
            .Build();

        Modal.AddControl(_bodyEditor);

        // Help text
        var helpText = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]Ctrl+Enter: Send  |  Ctrl+S: Save Draft  |  Esc: Discard[/]")
            .Build();
        helpText.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(helpText);
    }

    protected override void SetInitialFocus()
    {
        if (string.IsNullOrEmpty(_initialTo))
            _toField?.RequestFocus();
        else
            _bodyEditor?.RequestFocus();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);

        if (ctrl && e.KeyInfo.Key == ConsoleKey.Enter)
        {
            // Send
            var to = _toField?.Input ?? "";
            if (string.IsNullOrWhiteSpace(to))
                return; // Don't send without recipient

            CloseWithResult(new ComposeResult(
                To: to,
                Cc: string.IsNullOrWhiteSpace(_ccField?.Input) ? null : _ccField.Input,
                Subject: _subjectField?.Input ?? "",
                Body: _bodyEditor?.Content ?? ""));

            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
