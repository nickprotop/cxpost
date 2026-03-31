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
        // Header
        Modal.AddControl(Controls.Markup()
            .AddLine("[cyan1 bold]✉  New Message[/]")
            .AddLine("[grey70]Compose and send an email[/]")
            .WithMargin(2, 2, 2, 0)
            .Build());

        // Header separator
        var headerRule = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        headerRule.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(headerRule);

        // To field
        var toLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]To:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();
        toLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(toLabel);

        _toField = new PromptControl { Prompt = "", Input = _initialTo };
        _toField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _toField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_toField);

        // Cc field
        var ccLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Cc:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();
        ccLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(ccLabel);

        _ccField = new PromptControl { Prompt = "", Input = "" };
        _ccField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _ccField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_ccField);

        // Subject field
        var subjectLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Subject:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();
        subjectLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Modal.AddControl(subjectLabel);

        _subjectField = new PromptControl { Prompt = "", Input = _initialSubject };
        _subjectField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _subjectField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_subjectField);

        // Separator before body
        var bodyRule = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        bodyRule.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(bodyRule);

        // Body editor
        _bodyEditor = Controls.MultilineEdit(_initialBody)
            .WithWrapMode(WrapMode.WrapWords)
            .WithPlaceholder("Type your message here...")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithMargin(2, 0, 2, 0)
            .IsEditing()
            .Build();

        Modal.AddControl(_bodyEditor);

        // Help text
        var helpText = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]S: Send  |  Esc: Discard[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 2, 0)
            .StickyBottom()
            .Build();
        Modal.AddControl(helpText);

        // Rule before buttons
        var bottomRule = Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build();
        bottomRule.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(bottomRule);

        // Button row
        var sendButton = Controls.Button("[grey93]  Send (S)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) => TrySend())
            .Build();

        var discardButton = Controls.Button("[grey93]  Discard (Esc)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .OnClick((s, e) => CloseWithResult(null))
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 2, 0)
            .StickyBottom()
            .Column(col => col.Add(sendButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(discardButton))
            .Build();
        Modal.AddControl(buttonGrid);
    }

    private void TrySend()
    {
        var to = _toField?.Input ?? "";
        if (string.IsNullOrWhiteSpace(to))
            return;

        CloseWithResult(new ComposeResult(
            To: to,
            Cc: string.IsNullOrWhiteSpace(_ccField?.Input) ? null : _ccField.Input,
            Subject: _subjectField?.Input ?? "",
            Body: _bodyEditor?.Content ?? ""));
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
        if (e.KeyInfo.Key == ConsoleKey.S && !IsEditing())
        {
            TrySend();
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    private bool IsEditing()
    {
        // Don't intercept 'S' when user is typing in a field
        return _bodyEditor?.IsEditing == true
            || _toField?.HasFocus == true
            || _ccField?.HasFocus == true
            || _subjectField?.HasFocus == true;
    }
}
