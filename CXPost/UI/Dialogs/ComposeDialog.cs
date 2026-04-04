using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using CXPost.Services;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public record ComposeResult(
    string AccountId,
    string FromName,
    string To,
    string? Cc,
    string Subject,
    string Body,
    List<string> AttachmentPaths,
    bool IncludeOriginalAttachments);

public class ComposeDialog : DialogBase<ComposeResult?>
{
    private readonly IContactsService _contacts;
    private readonly List<Models.Account> _accounts;
    private readonly int _initialAccountIndex;
    private readonly string _initialTo;
    private readonly string _initialCc;
    private readonly string _initialSubject;
    private readonly string _initialBody;

    private DropdownControl? _fromDropdown;
    private PromptControl? _fromNameField;
    private PromptControl? _toField;
    private PromptControl? _ccField;
    private PromptControl? _subjectField;
    private MultilineEditControl? _bodyEditor;

    private readonly List<string> _attachmentPaths = [];
    private readonly object _attachmentLock = new();
    private ScrollablePanelControl? _attachmentPanel;
    private readonly bool _isForwardMode;
    private readonly List<Models.AttachmentInfo>? _originalAttachments;
    private CheckboxControl? _includeOriginalAttachments;

    public ComposeDialog(
        IContactsService contacts,
        List<Models.Account> accounts,
        string? defaultAccountId = null,
        string to = "",
        string cc = "",
        string subject = "",
        string body = "",
        bool isForwardMode = false,
        List<Models.AttachmentInfo>? originalAttachments = null)
    {
        _contacts = contacts;
        _accounts = accounts;
        _initialAccountIndex = string.IsNullOrEmpty(defaultAccountId)
            ? 0
            : Math.Max(0, accounts.FindIndex(a => a.Id == defaultAccountId));
        _initialTo = to;
        _initialCc = cc;
        _initialSubject = subject;
        _initialBody = body;
        _isForwardMode = isForwardMode;
        _originalAttachments = originalAttachments;
    }

    private Models.Account? SelectedAccount =>
        _fromDropdown != null && _fromDropdown.SelectedIndex >= 0 && _fromDropdown.SelectedIndex < _accounts.Count
            ? _accounts[_fromDropdown.SelectedIndex]
            : _accounts.Count > 0 ? _accounts[0] : null;

    protected override string GetTitle() => "New Message";
    protected override (int width, int height) GetSize() => (80, 30);
    protected override bool GetResizable() => true;
    protected override bool GetMaximizable() => true;
    protected override ComposeResult? GetDefaultResult() => null;

    protected override void PlayEnterAnimation()
    {
        WindowAnimations.SlideIn(Modal, SlideDirection.Bottom, TimeSpan.FromMilliseconds(180),
            EasingFunctions.EaseOut);
    }

    protected override void BuildContent()
    {
        // ── Header ──────────────────────────────────────────────────────
        Modal.AddControl(Controls.Markup()
            .AddLine("[cyan1 bold]\u2709  New Message[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

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
            var initialFromName = !string.IsNullOrEmpty(initialAccount.FromName) ? initialAccount.FromName : initialAccount.Name;
            _fromNameField = new PromptControl { Prompt = "[grey70]Name:[/]    ", Input = initialFromName };
            _fromNameField.HorizontalAlignment = HorizontalAlignment.Stretch;
            _fromNameField.Margin = new Margin(2, 0, 2, 0);
            Modal.AddControl(_fromNameField);

            // Update name field when account changes
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
        _toField = new PromptControl { Prompt = "[grey70]To:[/]      ", Input = _initialTo };
        _toField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _toField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_toField);

        _ccField = new PromptControl { Prompt = "[grey70]Cc:[/]      ", Input = _initialCc };
        _ccField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _ccField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_ccField);

        _subjectField = new PromptControl { Prompt = "[grey70]Subject:[/] ", Input = _initialSubject };
        _subjectField.HorizontalAlignment = HorizontalAlignment.Stretch;
        _subjectField.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(_subjectField);

        // ── Attachment section (hidden until F2) ────────────────────────
        _attachmentPanel = Controls.ScrollablePanel()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build();
        _attachmentPanel.Visible = false;
        Modal.AddControl(_attachmentPanel);

        // ── Forward attachment toggle (only in forward mode) ────────────
        if (_isForwardMode && _originalAttachments != null && _originalAttachments.Count > 0)
        {
            var totalSize = _originalAttachments.Sum(a => a.Size);
            var sizeStr = FormatFileSize(totalSize);
            _includeOriginalAttachments = Controls.Checkbox($"Include original attachments ({_originalAttachments.Count}, {sizeStr})")
                .Checked(true)
                .WithMargin(2, 1, 2, 0)
                .Build();
            Modal.AddControl(_includeOriginalAttachments);
        }

        // ── Body separator ──────────────────────────────────────────────
        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0).Build());

        // ── Body editor ─────────────────────────────────────────────────
        _bodyEditor = Controls.MultilineEdit(_initialBody)
            .WithWrapMode(WrapMode.WrapWords)
            .WithPlaceholder("Type your message here...")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithMargin(2, 0, 2, 0)
            .IsEditing()
            .Build();
        Modal.AddControl(_bodyEditor);

        // ── Bottom toolbar ──────────────────────────────────────────────
        Modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23)
            .StickyBottom().WithMargin(2, 0, 2, 0).Build());

        var sendButton = Controls.Button("[grey93]Send [cyan1](S)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) => TrySend())
            .Build();

        var attachButton = Controls.Button("[grey93]Attach [cyan1](F2)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .WithFocusedBackgroundColor(Color.SteelBlue)
            .OnClick((s, e) => AddAttachment())
            .Build();

        var discardButton = Controls.Button("[grey93]Discard [cyan1](Esc)[/][/]")
            .WithBackgroundColor(Color.Transparent)
            .OnClick((s, e) => CloseWithResult(null))
            .Build();

        var toolbar = Controls.Toolbar()
            .AddButton(sendButton)
            .AddButton(attachButton)
            .AddSeparator(1)
            .AddButton(discardButton)
            .WithSpacing(1)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(toolbar);
    }

    private void TrySend()
    {
        var to = _toField?.Input ?? "";
        if (string.IsNullOrWhiteSpace(to))
            return;

        var account = SelectedAccount;
        if (account == null) return;

        CloseWithResult(new ComposeResult(
            AccountId: account.Id,
            FromName: _fromNameField?.Input?.Trim() ?? account.Name,
            To: to,
            Cc: string.IsNullOrWhiteSpace(_ccField?.Input) ? null : _ccField.Input,
            Subject: _subjectField?.Input ?? "",
            Body: _bodyEditor?.Content ?? "",
            AttachmentPaths: new List<string>(_attachmentPaths),
            IncludeOriginalAttachments: _includeOriginalAttachments?.Checked ?? false));
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
        if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            AddAttachment();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.S && !IsEditing())
        {
            TrySend();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Escape && e.AlreadyHandled)
        {
            // A control (e.g. multiline editor) already handled Escape — don't close
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

    // ── Attachment management ───────────────────────────────────────────

    private void AddAttachment()
    {
        _ = Task.Run(async () =>
        {
            var path = await SharpConsoleUI.Dialogs.FileDialogs.ShowFilePickerAsync(WindowSystem, parentWindow: Modal);
            if (path != null && File.Exists(path))
            {
                lock (_attachmentLock)
                {
                    _attachmentPaths.Add(path);
                }
                RefreshAttachmentUI();
            }
        });
    }

    private void RemoveAttachment(int index)
    {
        lock (_attachmentLock)
        {
            if (index >= 0 && index < _attachmentPaths.Count)
                _attachmentPaths.RemoveAt(index);
            else
                return;
        }
        RefreshAttachmentUI();
    }

    private void RemoveAllAttachments()
    {
        lock (_attachmentLock)
        {
            _attachmentPaths.Clear();
        }
        RefreshAttachmentUI();
    }

    private void RefreshAttachmentUI()
    {
        if (_attachmentPanel == null) return;

        if (_attachmentPaths.Count == 0)
        {
            _attachmentPanel.Visible = false;
            Modal.Invalidate(true);
            return;
        }

        _attachmentPanel.Visible = true;
        _attachmentPanel.ClearContents();

        // Header
        _attachmentPanel.AddControl(Controls.Markup(
            $"  [{ColorScheme.PrimaryMarkup}]\U0001f4ce Attachments ({_attachmentPaths.Count})[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        // Each attachment
        for (var i = 0; i < _attachmentPaths.Count; i++)
        {
            var idx = i;
            var fileName = Path.GetFileName(_attachmentPaths[i]);
            long fileSize = 0;
            try { fileSize = new FileInfo(_attachmentPaths[i]).Length; } catch { }
            var sizeStr = FormatFileSize(fileSize);

            var bar = Controls.StatusBar()
                .AddLeftText($"  {MarkupParser.Escape(fileName)}  [grey50]{sizeStr}[/]")
                .AddRightText($"[{ColorScheme.ErrorMarkup}]\u2715 Remove[/]", () => RemoveAttachment(idx))
                .WithMargin(2, 0, 2, 0)
                .Build();
            bar.BackgroundColor = Color.Transparent;
            _attachmentPanel.AddControl(bar);
        }

        // Action bar
        if (_attachmentPaths.Count > 1)
        {
            var actionBar = Controls.StatusBar()
                .AddLeftText($"[{ColorScheme.ErrorMarkup}]Remove All[/]", () => RemoveAllAttachments())
                .WithMargin(2, 0, 2, 0)
                .Build();
            actionBar.BackgroundColor = Color.Transparent;
            _attachmentPanel.AddControl(actionBar);
        }

        Modal.Invalidate(true);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
