using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class AccountSettingsDialog : DialogBase<Account?>
{
    private readonly Account? _existing;

    // General tab
    private PromptControl? _nameField;
    private PromptControl? _emailField;
    private PromptControl? _usernameField;
    private PromptControl? _replyToField;
    private PromptControl? _passwordField;

    // Server tab
    private PromptControl? _imapHostField;
    private PromptControl? _imapPortField;
    private PromptControl? _smtpHostField;
    private PromptControl? _smtpPortField;

    // Compose tab
    private MultilineEditControl? _signatureEditor;
    private CheckboxControl? _sigAboveQuoteCheckbox;
    private PromptControl? _autoBccField;
    private PromptControl? _defaultCcField;
    private PromptControl? _replyPrefixField;
    private PromptControl? _forwardPrefixField;

    // Sync tab
    private PromptControl? _syncIntervalField;
    private PromptControl? _maxMessagesField;
    private CheckboxControl? _markAsReadCheckbox;
    private CheckboxControl? _notificationsCheckbox;

    private TabControl? _tabControl;

    public AccountSettingsDialog(Account? existing = null)
    {
        _existing = existing;
    }

    protected override string GetTitle() => _existing != null ? "Account Settings" : "Add Account";
    protected override (int width, int height) GetSize() => (70, 30);
    protected override bool GetResizable() => true;
    protected override Account? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var title = _existing != null ? "Account Settings" : "Add Account";
        var subtitle = _existing != null
            ? $"Editing {_existing.Name} ({_existing.Email})"
            : "Configure your new email account";
        Modal.AddControl(Controls.Markup()
            .AddLine($"[cyan1 bold]\u2709  {title}[/]")
            .AddLine($"[grey70]{subtitle}[/]")
            .WithMargin(2, 2, 2, 0)
            .Build());

        var separator = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        separator.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(separator);

        _tabControl = Controls.TabControl()
            .AddTab("General", BuildGeneralTab())
            .AddTab("Server", BuildServerTab())
            .AddTab("Compose", BuildComposeTab())
            .AddTab("Sync", BuildSyncTab())
            .WithActiveTab(0)
            .WithHeaderStyle(TabHeaderStyle.Separator)
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .WithMargin(2, 1, 2, 0)
            .Fill()
            .Build();
        Modal.AddControl(_tabControl);

        var bottomRule = Controls.RuleBuilder().StickyBottom().WithColor(Color.Grey23).Build();
        bottomRule.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(bottomRule);

        Modal.AddControl(Controls.Markup(
            "[grey50]Tab/Shift+Tab: Switch tabs  |  Enter: Save  |  Esc: Cancel[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 2, 0)
            .StickyBottom()
            .Build());

        var saveButton = Controls.Button("[grey93]  Save (Enter)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) => TrySave())
            .Build();

        var cancelButton = Controls.Button("[grey93]  Cancel (Esc)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .OnClick((s, e) => CloseWithResult(null))
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(saveButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(buttonGrid);
    }

    private ScrollablePanelControl BuildGeneralTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        AddFieldToPanel(panel, "Display Name", ref _nameField, _existing?.Name ?? "");
        AddFieldToPanel(panel, "Email Address", ref _emailField, _existing?.Email ?? "");
        AddFieldToPanel(panel, "Username (if different from email)", ref _usernameField, _existing?.Username ?? "");
        AddFieldToPanel(panel, "Reply-To Address", ref _replyToField, _existing?.ReplyToAddress ?? "");
        AddFieldToPanel(panel, "Password", ref _passwordField, "");
        if (_passwordField != null)
            _passwordField.MaskCharacter = '*';

        panel.AddControl(Controls.Markup()
            .AddEmptyLine()
            .AddLine("[grey50]Leave Reply-To empty to use the email address[/]")
            .WithMargin(1, 0, 1, 0)
            .Build());

        return panel;
    }

    private ScrollablePanelControl BuildServerTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        panel.AddControl(Controls.Markup("[grey70 bold]Incoming Mail (IMAP)[/]")
            .WithMargin(1, 1, 1, 0).Build());
        AddFieldToPanel(panel, "IMAP Host", ref _imapHostField, _existing?.ImapHost ?? "");
        AddFieldToPanel(panel, "IMAP Port", ref _imapPortField, _existing?.ImapPort.ToString() ?? "993");

        panel.AddControl(Controls.Markup("[grey70 bold]Outgoing Mail (SMTP)[/]")
            .WithMargin(1, 1, 1, 0).Build());
        AddFieldToPanel(panel, "SMTP Host", ref _smtpHostField, _existing?.SmtpHost ?? "");
        AddFieldToPanel(panel, "SMTP Port", ref _smtpPortField, _existing?.SmtpPort.ToString() ?? "587");

        return panel;
    }

    private ScrollablePanelControl BuildComposeTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        panel.AddControl(Controls.Markup("[grey70 bold]Signature[/]")
            .WithMargin(1, 1, 1, 0).Build());

        _signatureEditor = Controls.MultilineEdit(_existing?.Signature ?? "")
            .WithWrapMode(WrapMode.WrapWords)
            .WithPlaceholder("Your email signature...")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithViewportHeight(5)
            .WithMargin(1, 0, 1, 0)
            .IsEditing()
            .Build();
        panel.AddControl(_signatureEditor);

        _sigAboveQuoteCheckbox = Controls.Checkbox("Place signature above quoted text")
            .Checked(_existing?.SignaturePosition == SignaturePosition.AboveQuote)
            .WithMargin(1, 1, 1, 0)
            .Build();
        panel.AddControl(_sigAboveQuoteCheckbox);

        var composeRule = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        composeRule.Margin = new Margin(1, 1, 1, 0);
        panel.AddControl(composeRule);

        panel.AddControl(Controls.Markup("[grey70 bold]Defaults[/]")
            .WithMargin(1, 1, 1, 0).Build());
        AddFieldToPanel(panel, "Auto-Bcc", ref _autoBccField, _existing?.AutoBcc ?? "");
        AddFieldToPanel(panel, "Default Cc", ref _defaultCcField, _existing?.DefaultCc ?? "");
        AddFieldToPanel(panel, "Reply Prefix", ref _replyPrefixField, _existing?.ReplyPrefix ?? "Re:");
        AddFieldToPanel(panel, "Forward Prefix", ref _forwardPrefixField, _existing?.ForwardPrefix ?? "Fwd:");

        return panel;
    }

    private ScrollablePanelControl BuildSyncTab()
    {
        var panel = Controls.ScrollablePanel()
            .WithScrollbar(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        panel.AddControl(Controls.Markup("[grey70 bold]Synchronization[/]")
            .WithMargin(1, 1, 1, 0).Build());
        AddFieldToPanel(panel, "Sync Interval (seconds)", ref _syncIntervalField,
            (_existing?.SyncIntervalSeconds ?? 300).ToString());
        AddFieldToPanel(panel, "Max Messages per Folder (0 = unlimited)", ref _maxMessagesField,
            (_existing?.MaxMessagesPerFolder ?? 0).ToString());

        var syncRule = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        syncRule.Margin = new Margin(1, 1, 1, 0);
        panel.AddControl(syncRule);

        panel.AddControl(Controls.Markup("[grey70 bold]Behavior[/]")
            .WithMargin(1, 1, 1, 0).Build());

        _markAsReadCheckbox = Controls.Checkbox("Mark messages as read when viewed")
            .Checked(_existing?.MarkAsReadOnView ?? true)
            .WithMargin(1, 1, 1, 0)
            .Build();
        panel.AddControl(_markAsReadCheckbox);

        _notificationsCheckbox = Controls.Checkbox("Enable notifications for this account")
            .Checked(_existing?.NotificationsEnabled ?? true)
            .WithMargin(1, 0, 1, 0)
            .Build();
        panel.AddControl(_notificationsCheckbox);

        return panel;
    }

    private void AddFieldToPanel(ScrollablePanelControl panel, string label, ref PromptControl? field, string value)
    {
        panel.AddControl(Controls.Markup($"[{ColorScheme.MutedMarkup}]{label}:[/]")
            .WithMargin(1, 0, 1, 0).Build());
        field = new PromptControl { Prompt = "", Input = value };
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        field.Margin = new Margin(1, 0, 1, 1);
        panel.AddControl(field);
    }

    private void TrySave()
    {
        var account = _existing ?? new Account();
        account.Name = _nameField?.Input ?? "";
        account.Email = _emailField?.Input ?? "";
        account.Username = !string.IsNullOrWhiteSpace(_usernameField?.Input) ? _usernameField.Input : account.Email;
        account.ReplyToAddress = _replyToField?.Input ?? "";

        account.ImapHost = _imapHostField?.Input ?? "";
        account.ImapPort = int.TryParse(_imapPortField?.Input, out var ip) ? ip : 993;
        account.SmtpHost = _smtpHostField?.Input ?? "";
        account.SmtpPort = int.TryParse(_smtpPortField?.Input, out var sp) ? sp : 587;

        account.Signature = _signatureEditor?.Content ?? "";
        account.SignaturePosition = (_sigAboveQuoteCheckbox?.Checked ?? false)
            ? SignaturePosition.AboveQuote : SignaturePosition.BelowQuote;
        account.AutoBcc = _autoBccField?.Input ?? "";
        account.DefaultCc = _defaultCcField?.Input ?? "";
        account.ReplyPrefix = _replyPrefixField?.Input ?? "Re:";
        account.ForwardPrefix = _forwardPrefixField?.Input ?? "Fwd:";

        account.SyncIntervalSeconds = int.TryParse(_syncIntervalField?.Input, out var si) ? si : 300;
        account.MaxMessagesPerFolder = int.TryParse(_maxMessagesField?.Input, out var mm) ? mm : 0;
        account.MarkAsReadOnView = _markAsReadCheckbox?.Checked ?? true;
        account.NotificationsEnabled = _notificationsCheckbox?.Checked ?? true;

        CloseWithResult(account);
    }

    protected override void SetInitialFocus() => _nameField?.RequestFocus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter && !IsEditing())
        {
            TrySave();
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    private bool IsEditing()
    {
        return _signatureEditor?.IsEditing == true
            || _nameField?.HasFocus == true
            || _emailField?.HasFocus == true
            || _usernameField?.HasFocus == true
            || _replyToField?.HasFocus == true
            || _passwordField?.HasFocus == true
            || _imapHostField?.HasFocus == true
            || _imapPortField?.HasFocus == true
            || _smtpHostField?.HasFocus == true
            || _smtpPortField?.HasFocus == true
            || _autoBccField?.HasFocus == true
            || _defaultCcField?.HasFocus == true
            || _replyPrefixField?.HasFocus == true
            || _forwardPrefixField?.HasFocus == true
            || _syncIntervalField?.HasFocus == true
            || _maxMessagesField?.HasFocus == true;
    }

    public string? GetPassword() => _passwordField?.Input;
}
