using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class AccountSetupDialog : DialogBase<Account?>
{
    private readonly Account? _existing;
    private PromptControl? _nameField;
    private PromptControl? _emailField;
    private PromptControl? _imapHostField;
    private PromptControl? _imapPortField;
    private PromptControl? _smtpHostField;
    private PromptControl? _smtpPortField;
    private PromptControl? _passwordField;

    public AccountSetupDialog(Account? existing = null)
    {
        _existing = existing;
    }

    protected override string GetTitle() => _existing != null ? "Edit Account" : "Add Account";
    protected override (int width, int height) GetSize() => (60, 24);
    protected override Account? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        void AddField(string label, ref PromptControl? field, string value = "")
        {
            Modal.AddControl(Controls.Markup($"[{ColorScheme.MutedMarkup}]{label}:[/]").Build());
            field = new PromptControl { Prompt = "", Input = value };
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
            Modal.AddControl(field);
        }

        AddField("Display Name", ref _nameField, _existing?.Name ?? "");
        AddField("Email Address", ref _emailField, _existing?.Email ?? "");
        AddField("IMAP Host", ref _imapHostField, _existing?.ImapHost ?? "");
        AddField("IMAP Port", ref _imapPortField, _existing?.ImapPort.ToString() ?? "993");
        AddField("SMTP Host", ref _smtpHostField, _existing?.SmtpHost ?? "");
        AddField("SMTP Port", ref _smtpPortField, _existing?.SmtpPort.ToString() ?? "587");
        AddField("Password", ref _passwordField, "");

        if (_passwordField != null)
            _passwordField.MaskCharacter = '*';

        var help = Controls.Markup(
            $"[{ColorScheme.MutedMarkup}]Ctrl+Enter: Save  |  Esc: Cancel[/]").Build();
        help.StickyPosition = StickyPosition.Bottom;
        Modal.AddControl(help);
    }

    protected override void SetInitialFocus() => _nameField?.RequestFocus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var account = _existing ?? new Account();
            account.Name = _nameField?.Input ?? "";
            account.Email = _emailField?.Input ?? "";
            account.ImapHost = _imapHostField?.Input ?? "";
            account.ImapPort = int.TryParse(_imapPortField?.Input, out var ip) ? ip : 993;
            account.SmtpHost = _smtpHostField?.Input ?? "";
            account.SmtpPort = int.TryParse(_smtpPortField?.Input, out var sp) ? sp : 587;
            account.Username = account.Email;

            // Password stored separately via CredentialService
            // The caller handles this
            CloseWithResult(account);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    public string? GetPassword() => _passwordField?.Input;
}
