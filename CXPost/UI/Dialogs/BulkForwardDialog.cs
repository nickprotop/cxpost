using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using CXPost.Coordinators;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class BulkForwardDialog : DialogBase<bool>
{
    private readonly List<MailMessage> _messages;
    private readonly List<Account> _accounts;
    private readonly int _initialAccountIndex;
    private readonly ComposeCoordinator _composeCoordinator;
    private readonly IContactsService _contacts;
    private readonly Action<string> _onProgress;
    private readonly Action<string> _onSuccess;
    private readonly Action<string> _onError;
    private readonly CancellationToken _ct;

    private DropdownControl? _fromDropdown;
    private PromptControl? _fromNameField;
    private PromptControl? _toField;
    private PromptControl? _ccField;
    private PromptControl? _subjectField;
    private MultilineEditControl? _bodyEditor;
    private CheckboxControl? _sendIndividuallyToggle;
    private CheckboxControl? _includeAttachmentsToggle;

    private bool _isSending;

    // Autocomplete state
    private AutocompletePortalContent? _autocompletePortal;
    private LayoutNode? _autocompletePortalNode;
    private PromptControl? _activeAutocompletePrompt;

    public BulkForwardDialog(
        List<MailMessage> messages,
        List<Account> accounts,
        string? defaultAccountId,
        ComposeCoordinator composeCoordinator,
        IContactsService contacts,
        Action<string> onProgress,
        Action<string> onSuccess,
        Action<string> onError,
        CancellationToken ct)
    {
        _messages = messages;
        _accounts = accounts;
        _composeCoordinator = composeCoordinator;
        _contacts = contacts;
        _onProgress = onProgress;
        _onSuccess = onSuccess;
        _onError = onError;
        _ct = ct;
        _initialAccountIndex = string.IsNullOrEmpty(defaultAccountId)
            ? 0
            : Math.Max(0, accounts.FindIndex(a => a.Id == defaultAccountId));
    }

    private Account? SelectedAccount =>
        _fromDropdown != null && _fromDropdown.SelectedIndex >= 0 && _fromDropdown.SelectedIndex < _accounts.Count
            ? _accounts[_fromDropdown.SelectedIndex]
            : _accounts.Count > 0 ? _accounts[0] : null;

    protected override string GetTitle() => "";
    protected override (int width, int height) GetSize() => (80, 30);
    protected override bool GetResizable() => true;
    protected override bool GetMaximizable() => true;
    protected override bool GetDefaultResult() => false;

    protected override void PlayEnterAnimation()
    {
        WindowAnimations.SlideIn(Modal, SlideDirection.Bottom, TimeSpan.FromMilliseconds(180),
            EasingFunctions.EaseOut);
    }

    protected override void BuildContent()
    {
        // ── Header ──────────────────────────────────────────────────────
        Modal.AddControl(Controls.Markup()
            .AddLine($"[cyan1 bold]\u21b3  Forward {_messages.Count} Messages[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0).Build());

        // ── Message summary ─────────────────────────────────────────────
        var summaryPanel = Controls.ScrollablePanel()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build();
        summaryPanel.Margin = new Margin(2, 0, 2, 0);

        foreach (var msg in _messages)
        {
            var from = MarkupParser.Escape(msg.FromName ?? msg.FromAddress ?? "Unknown");
            var subject = MarkupParser.Escape(msg.Subject ?? "(no subject)");
            var date = msg.Date.ToString("MMM d");
            var attachIcon = msg.HasAttachments ? " \U0001f4ce" : "";
            var line = Controls.Markup(
                $"[grey50]{from}[/]  [{ColorScheme.MutedMarkup}]{subject}[/]  [grey42]{date}[/]{attachIcon}")
                .Build();
            summaryPanel.AddControl(line);
        }
        Modal.AddControl(summaryPanel);

        // ── Toggles ───��─────────────────────────────────────────────────
        _sendIndividuallyToggle = Controls.Checkbox("Send as individual emails")
            .Checked(false)
            .WithMargin(2, 1, 2, 0)
            .Build();
        Modal.AddControl(_sendIndividuallyToggle);

        var hasAnyAttachments = _messages.Any(m => m.HasAttachments);
        if (hasAnyAttachments)
        {
            var totalSize = ComposeCoordinator.GetTotalAttachmentSize(_messages);
            var sizeStr = FormatFileSize(totalSize);
            _includeAttachmentsToggle = Controls.Checkbox($"Include original attachments ({sizeStr})")
                .Checked(true)
                .WithMargin(2, 0, 2, 0)
                .Build();
            Modal.AddControl(_includeAttachmentsToggle);
        }

        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0).Build());

        // ── From (account dropdown + editable name) ────────────────────
        if (_accounts.Count > 0)
        {
            Modal.AddControl(Controls.Markup("[grey70]From:[/]")
                .WithMargin(2, 0, 2, 0).Build());

            var fromOptions = _accounts.Select(a => a.Email).ToArray();
            _fromDropdown = new DropdownControl("", fromOptions)
            {
                SelectedIndex = _initialAccountIndex,
                Margin = new Margin(2, 0, 2, 0)
            };
            Modal.AddControl(_fromDropdown);

            var initialAccount = _accounts[_initialAccountIndex];
            var initialFromName = !string.IsNullOrEmpty(initialAccount.FromName)
                ? initialAccount.FromName : initialAccount.Name;
            _fromNameField = new PromptControl { Prompt = "[grey70]Name:[/]    ", Input = initialFromName };
            _fromNameField.HorizontalAlignment = HorizontalAlignment.Stretch;
            _fromNameField.Margin = new Margin(2, 0, 2, 0);
            Modal.AddControl(_fromNameField);

            _fromDropdown.SelectedIndexChanged += (_, idx) =>
            {
                if (idx >= 0 && idx < _accounts.Count && _fromNameField != null)
                {
                    var acct = _accounts[idx];
                    _fromNameField.Input = !string.IsNullOrEmpty(acct.FromName) ? acct.FromName : acct.Name;
                }
            };
        }

        // ── Address fields ──────────────────────────────────────────────
        _toField = new PromptControl { Prompt = "[grey70]To:[/]      ", Input = "" };
        _toField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _toField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_toField);

        _ccField = new PromptControl { Prompt = "[grey70]Cc:[/]      ", Input = "" };
        _ccField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _ccField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_ccField);

        var defaultSubject = SelectedAccount != null
            ? MessageFormatter.GetBulkForwardSubject(_messages, SelectedAccount.ForwardPrefix)
            : MessageFormatter.GetBulkForwardSubject(_messages);
        _subjectField = new PromptControl { Prompt = "[grey70]Subject:[/] ", Input = defaultSubject };
        _subjectField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _subjectField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_subjectField);

        // ── Body separator ─────���────────────────────────────────────────
        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0).Build());

        // ── Body editor ─────────────────────────────────────────────────
        _bodyEditor = Controls.MultilineEdit("")
            .WithWrapMode(WrapMode.WrapWords)
            .WithPlaceholder("Type an intro (optional)...")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithMargin(2, 0, 2, 0)
            .IsEditing()
            .Build();
        Modal.AddControl(_bodyEditor);

        // ── Bottom toolbar ─────────��────────────────────────────────────
        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .StickyBottom().WithMargin(2, 0, 2, 0).Build());

        var sendButton = Controls.Button("[grey93]Send [cyan1](S)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) => TrySend())
            .Build();

        var discardButton = Controls.Button("[grey93]Discard [cyan1](Esc)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .OnClick((s, e) => CloseWithResult(false))
            .Build();

        var toolbar = Controls.Toolbar()
            .AddButton(sendButton)
            .AddSeparator(1)
            .AddButton(discardButton)
            .WithSpacing(1)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(toolbar);

        // ── Autocomplete hooks ──────────────────────────────────────────
        _toField.InputChanged += (_, text) => OnPromptInputChanged(_toField, text);
        _ccField.InputChanged += (_, text) => OnPromptInputChanged(_ccField, text);
        Modal.PreviewKeyPressed += OnPreviewKey;
    }

    private void TrySend()
    {
        var to = _toField?.Input ?? "";
        if (string.IsNullOrWhiteSpace(to) || _isSending) return;

        var account = SelectedAccount;
        if (account == null) return;

        _isSending = true;
        var sendIndividually = _sendIndividuallyToggle?.Checked ?? false;
        var includeAttachments = _includeAttachmentsToggle?.Checked ?? false;
        var fromName = _fromNameField?.Input?.Trim() ?? account.Name;
        var cc = string.IsNullOrWhiteSpace(_ccField?.Input) ? null : _ccField.Input;
        var subject = _subjectField?.Input ?? "";
        var intro = _bodyEditor?.Content ?? "";

        CloseWithResult(true);

        _ = Task.Run(async () => await SendAsync(
            account, fromName, to, cc, subject, intro,
            sendIndividually, includeAttachments), _ct);
    }

    private async Task SendAsync(
        Account account, string fromName, string to, string? cc,
        string subject, string intro, bool sendIndividually, bool includeAttachments)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cxpost-fwd-{Guid.NewGuid():N}");

        try
        {
            if (includeAttachments)
            {
                var totalSize = ComposeCoordinator.GetTotalAttachmentSize(_messages);
                if (totalSize > 20 * 1024 * 1024)
                {
                    var sizeMb = totalSize / (1024.0 * 1024.0);
                    var confirm = await new ConfirmDialog(
                        "Large Attachments",
                        $"Total attachment size is {sizeMb:F1} MB. Continue sending?")
                        .ShowAsync(WindowSystem);
                    if (!confirm)
                    {
                        _onProgress("Forward cancelled.");
                        return;
                    }
                }
            }

            if (sendIndividually)
                await SendIndividuallyAsync(account, fromName, to, cc, subject, intro, includeAttachments, tempDir);
            else
                await SendAsCombinedAsync(account, fromName, to, cc, subject, intro, includeAttachments, tempDir);
        }
        catch (OperationCanceledException)
        {
            _onError("Forward cancelled.");
        }
        catch (Exception ex)
        {
            _onError($"Forward failed: {ex.Message}");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    private async Task SendAsCombinedAsync(
        Account account, string fromName, string to, string? cc,
        string subject, string intro, bool includeAttachments, string tempDir)
    {
        var allAttachmentPaths = new List<string>();

        if (includeAttachments)
        {
            for (var i = 0; i < _messages.Count; i++)
            {
                if (!_messages[i].HasAttachments) continue;
                _onProgress($"Fetching attachments ({i + 1} of {_messages.Count})...");
                var paths = await _composeCoordinator.FetchMessageAttachmentsAsync(
                    account, _messages[i], tempDir, _ct);
                allAttachmentPaths.AddRange(paths);
            }
        }

        _onProgress("Sending forwarded message...");
        var body = MessageFormatter.FormatBulkForwardBody(intro, _messages);

        await _composeCoordinator.SendAsync(
            account, fromName, to, cc, subject, body, allAttachmentPaths, _ct);

        _onSuccess($"Forwarded {_messages.Count} messages to {to}");
    }

    private async Task SendIndividuallyAsync(
        Account account, string fromName, string to, string? cc,
        string subject, string intro, bool includeAttachments, string tempDir)
    {
        var sent = 0;
        for (var i = 0; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            _onProgress($"Sending message {i + 1} of {_messages.Count}...");

            var attachmentPaths = new List<string>();
            if (includeAttachments && msg.HasAttachments)
            {
                _onProgress($"Fetching attachments ({i + 1} of {_messages.Count})...");
                attachmentPaths = await _composeCoordinator.FetchMessageAttachmentsAsync(
                    account, msg, Path.Combine(tempDir, $"msg-{i}"), _ct);
            }

            var body = string.IsNullOrWhiteSpace(intro)
                ? MessageFormatter.FormatForwardBody(msg)
                : intro + "\n\n" + MessageFormatter.FormatForwardBody(msg);

            await _composeCoordinator.SendAsync(
                account, fromName, to, cc, subject, body, attachmentPaths, _ct);

            sent++;
        }

        _onSuccess($"Forwarded {sent} messages to {to}");
    }

    protected override void SetInitialFocus()
    {
        _toField?.RequestFocus();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.S && !IsEditing())
        {
            TrySend();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Spacebar
                 && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            var prompt = _toField?.HasFocus == true ? _toField
                       : _ccField?.HasFocus == true ? _ccField
                       : null;
            if (prompt != null)
            {
                OpenAutocomplete(prompt);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == ConsoleKey.Escape && e.AlreadyHandled)
        {
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    private bool IsEditing()
    {
        return _bodyEditor?.IsEditing == true
            || _fromNameField?.HasFocus == true
            || _toField?.HasFocus == true
            || _ccField?.HasFocus == true
            || _subjectField?.HasFocus == true;
    }

    private static void CleanupTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    // ── Autocomplete ────────────────────────────────────────────────

    protected override void OnCleanup()
    {
        CloseAutocomplete();
    }

    private void OnPreviewKey(object? sender, KeyPressedEventArgs e)
    {
        // ↓ in To/Cc prompt when portal is closed → open it
        if (e.KeyInfo.Key == ConsoleKey.DownArrow && _autocompletePortal == null)
        {
            var prompt = _toField?.HasFocus == true ? _toField
                       : _ccField?.HasFocus == true ? _ccField
                       : null;
            if (prompt != null)
            {
                OpenAutocomplete(prompt);
                e.Handled = true;
            }
            return;
        }

        if (_autocompletePortal == null || !_autocompletePortal.HasItems)
            return;

        if (_activeAutocompletePrompt?.HasFocus != true)
            return;

        if (e.KeyInfo.Key == ConsoleKey.UpArrow)
        {
            _autocompletePortal.MoveUp();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.DownArrow)
        {
            _autocompletePortal.MoveDown();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Enter || e.KeyInfo.Key == ConsoleKey.Tab)
        {
            var selected = _autocompletePortal.GetSelectedItem();
            if (selected != null)
            {
                InsertContact(selected);
                CloseAutocomplete();
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseAutocomplete();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Spacebar
                 && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            OpenAutocomplete(_activeAutocompletePrompt);
            e.Handled = true;
        }
    }

    private void OnPromptInputChanged(PromptControl prompt, string text)
    {
        var token = ExtractCurrentToken(text);
        if (token.Length >= 2)
        {
            var results = _contacts.Autocomplete(token);
            if (results.Count > 0)
                ShowAutocomplete(prompt, results);
            else
                CloseAutocomplete();
        }
        else if (_activeAutocompletePrompt == prompt)
        {
            CloseAutocomplete();
        }
    }

    private void OpenAutocomplete(PromptControl prompt)
    {
        var token = ExtractCurrentToken(prompt.Input ?? "");
        List<string> results;
        if (string.IsNullOrEmpty(token))
            results = _contacts.GetTopContacts(10);
        else
            results = _contacts.Autocomplete(token);

        if (results.Count > 0)
            ShowAutocomplete(prompt, results);
    }

    private void ShowAutocomplete(PromptControl prompt, List<string> results)
    {
        if (_autocompletePortal == null)
        {
            _autocompletePortal = new AutocompletePortalContent(prompt);
            _autocompletePortal.ItemSelected += OnAutocompleteItemSelected;
            _autocompletePortal.UpdateItems(results);
            _autocompletePortalNode = Modal.CreatePortal(prompt, _autocompletePortal);
            _activeAutocompletePrompt = prompt;
        }
        else if (_activeAutocompletePrompt == prompt)
        {
            _autocompletePortal.UpdateItems(results);
        }
        else
        {
            CloseAutocomplete();
            ShowAutocomplete(prompt, results);
        }
    }

    private void CloseAutocomplete()
    {
        if (_autocompletePortal != null && _autocompletePortalNode != null)
        {
            Modal.RemovePortal(_activeAutocompletePrompt!, _autocompletePortalNode);
            _autocompletePortal.ItemSelected -= OnAutocompleteItemSelected;
            _autocompletePortal = null;
            _autocompletePortalNode = null;
            _activeAutocompletePrompt = null;
        }
    }

    private void OnAutocompleteItemSelected(string item)
    {
        InsertContact(item);
        CloseAutocomplete();
    }

    private void InsertContact(string contact)
    {
        if (_activeAutocompletePrompt == null) return;

        var text = _activeAutocompletePrompt.Input ?? "";
        var lastComma = text.LastIndexOf(',');
        var prefix = lastComma >= 0 ? text[..(lastComma + 1)] + " " : "";
        _activeAutocompletePrompt.SetInput(prefix + contact + ", ");
    }

    private static string ExtractCurrentToken(string text)
    {
        var lastComma = text.LastIndexOf(',');
        var token = lastComma >= 0 ? text[(lastComma + 1)..] : text;
        return token.Trim();
    }
}
