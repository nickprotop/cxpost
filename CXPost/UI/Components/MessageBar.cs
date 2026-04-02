using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CXPost.UI.Components;

public enum MessageSeverity { Info, Success, Warning, Error }

public record MessageEntry(
    string Id,
    string Text,
    MessageSeverity Severity,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    bool Dismissable);

/// <summary>
/// Stacking transient message bar. Shows messages at the bottom of the window
/// above the help bar, with auto-timeout, manual dismiss, and replace support.
/// </summary>
public class MessageBar
{
    private readonly List<MessageEntry> _messages = [];
    private readonly MarkupControl _control;
    private readonly BaseControl _rule;
    private int _nextId;

    public MessageBar()
    {
        _rule = Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(Color.Grey23)
            .Build();
        _rule.Visible = false;

        _control = Controls.Markup("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(1, 0, 1, 0)
            .StickyBottom()
            .Build();
        _control.Visible = false;

        _control.MouseClick += (_, e) =>
        {
            // First check for undo actions
            for (var i = _messages.Count - 1; i >= 0; i--)
            {
                if (_undoActions.ContainsKey(_messages[i].Id))
                {
                    TryUndo(_messages[i].Id);
                    e.Handled = true;
                    return;
                }
            }
            // Otherwise dismiss the latest dismissable message
            for (var i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Dismissable)
                {
                    _messages.RemoveAt(i);
                    Render();
                    e.Handled = true;
                    return;
                }
            }
        };
    }

    public MarkupControl Control => _control;
    public BaseControl Rule => _rule;
    public bool HasMessages => _messages.Count > 0;

    public string Show(string text, MessageSeverity severity = MessageSeverity.Info,
        int? timeoutSeconds = 3, bool dismissable = true)
    {
        var id = $"msg-{_nextId++}";
        var entry = new MessageEntry(
            id, text, severity,
            DateTime.UtcNow,
            timeoutSeconds.HasValue ? DateTime.UtcNow.AddSeconds(timeoutSeconds.Value) : null,
            dismissable);
        _messages.Add(entry);
        Render();
        return id;
    }

    /// <summary>
    /// Replace an existing message by ID, or add if not found.
    /// Keeps the same position in the stack.
    /// </summary>
    public string Replace(string id, string text, MessageSeverity severity = MessageSeverity.Info,
        int? timeoutSeconds = null, bool dismissable = false)
    {
        var idx = _messages.FindIndex(m => m.Id == id);
        var entry = new MessageEntry(
            id, text, severity,
            DateTime.UtcNow,
            timeoutSeconds.HasValue ? DateTime.UtcNow.AddSeconds(timeoutSeconds.Value) : null,
            dismissable);

        if (idx >= 0)
            _messages[idx] = entry;
        else
            _messages.Add(entry);

        Render();
        return id;
    }

    public string ShowError(string text, int? timeoutSeconds = 5) =>
        Show(text, MessageSeverity.Error, timeoutSeconds);

    public string ShowSuccess(string text, int? timeoutSeconds = 3) =>
        Show(text, MessageSeverity.Success, timeoutSeconds);

    public string ShowWarning(string text, int? timeoutSeconds = 4) =>
        Show(text, MessageSeverity.Warning, timeoutSeconds);

    public string ShowInfo(string text, int? timeoutSeconds = 3) =>
        Show(text, MessageSeverity.Info, timeoutSeconds);

    /// <summary>
    /// Show a persistent non-dismissable message (e.g. during sync).
    /// Use Replace() to update it, then Dismiss() or replace with a dismissable version when done.
    /// </summary>
    public string ShowProgress(string text) =>
        Show(text, MessageSeverity.Info, timeoutSeconds: null, dismissable: false);

    public string ShowPersistent(string text, MessageSeverity severity = MessageSeverity.Info, bool dismissable = true) =>
        Show(text, severity, timeoutSeconds: null, dismissable: dismissable);

    public void Dismiss(string id)
    {
        _messages.RemoveAll(m => m.Id == id);
        Render();
    }

    private readonly Dictionary<string, Action> _undoActions = new();

    /// <summary>
    /// Shows a message with a clickable [Undo] action. The undo callback fires on click.
    /// </summary>
    public string ShowWithUndo(string id, string text, Action onUndo, int timeoutSeconds = 5)
    {
        _undoActions[id] = onUndo;
        var entry = new MessageEntry(
            id, text + $"  [{ColorScheme.PrimaryMarkup}][[Undo]][/]",
            MessageSeverity.Info,
            DateTime.UtcNow,
            DateTime.UtcNow.AddSeconds(timeoutSeconds),
            true);

        var idx = _messages.FindIndex(m => m.Id == id);
        if (idx >= 0)
            _messages[idx] = entry;
        else
            _messages.Add(entry);

        Render();
        return id;
    }

    /// <summary>
    /// Tries to invoke and dismiss an undo action by message ID.
    /// Returns true if an undo action was found and invoked.
    /// </summary>
    public bool TryUndo(string id)
    {
        if (_undoActions.Remove(id, out var action))
        {
            _messages.RemoveAll(m => m.Id == id);
            Render();
            action();
            return true;
        }
        return false;
    }

    public void DismissLatest()
    {
        if (_messages.Count > 0)
            _messages.RemoveAt(_messages.Count - 1);
        Render();
    }

    public void DismissAll()
    {
        _messages.Clear();
        Render();
    }

    /// <summary>
    /// Call from main loop to expire timed-out messages.
    /// </summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        var expired = _messages.Where(m => m.ExpiresAt.HasValue && m.ExpiresAt.Value <= now).ToList();
        foreach (var msg in expired)
            _undoActions.Remove(msg.Id);
        var removed = _messages.RemoveAll(m => m.ExpiresAt.HasValue && m.ExpiresAt.Value <= now);
        if (removed > 0)
            Render();
    }

    private void Render()
    {
        if (_messages.Count == 0)
        {
            _control.Visible = false;
            _rule.Visible = false;
            return;
        }

        _control.Visible = true;
        _rule.Visible = true;

        var lines = new List<string>();
        foreach (var msg in _messages)
        {
            var icon = msg.Severity switch
            {
                MessageSeverity.Success => $"[{ColorScheme.SuccessMarkup}]✓[/]",
                MessageSeverity.Warning => $"[{ColorScheme.FlaggedMarkup}]⚠[/]",
                MessageSeverity.Error   => $"[{ColorScheme.ErrorMarkup}]✗[/]",
                _                       => $"[{ColorScheme.PrimaryMarkup}]⟳[/]"
            };

            var textColor = msg.Severity switch
            {
                MessageSeverity.Error => ColorScheme.ErrorMarkup,
                MessageSeverity.Warning => ColorScheme.FlaggedMarkup,
                MessageSeverity.Success => ColorScheme.SuccessMarkup,
                _ => ColorScheme.MutedMarkup
            };

            var suffix = msg.Dismissable ? $" [{ColorScheme.MutedMarkup}](click to dismiss)[/]" : "";
            // Show only first line to prevent multiline blowup
            var displayText = msg.Text.Split('\n')[0].Trim();
            lines.Add($"{icon} [{textColor}]{displayText}[/]{suffix}");
        }

        _control.SetContent(lines);
    }
}
