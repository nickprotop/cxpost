using System.Collections.Concurrent;
using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Rendering;
using CXPost.Coordinators;
using CXPost.Models;
using CXPost.Services;
using CXPost.UI.Components;
using CXPost.UI.Dialogs;

namespace CXPost.UI;

public partial class CXPostApp
{
    private void UpdateToolbar()
    {
        if (_toolbar == null) return;

        _toolbar.Clear();

        var hasMessage = GetSelectedMessage() != null;
        var isDashboard = _dashboardPanel?.Visible == true;
        var checkedCount = GetCheckedCount();

        if (checkedCount > 0)
        {
            // Bulk mode toolbar — actions apply to checked messages
            AddToolbarButton($"\u2717 Delete ({checkedCount})", () => SimulateKey(ConsoleKey.Delete));
            AddToolbarButton($"\u2022 Read ({checkedCount})", () => SimulateKey(ConsoleKey.U, ctrl: true));
            AddToolbarButton($"\u2691 Flag ({checkedCount})", () => SimulateKey(ConsoleKey.D, ctrl: true));
            AddToolbarButton($"\u2192 Move ({checkedCount})", () => SimulateKey(ConsoleKey.M, ctrl: true));
            AddToolbarButton($"\u21aa Fwd ({checkedCount})", () => SimulateKey(ConsoleKey.F, ctrl: true));
            _toolbar.AddItem(new SeparatorControl());
            AddToolbarButton("\u2715 Clear", () => ClearSelection());
        }
        else
        {
            // Normal toolbar
            AddToolbarButton("\u2709 Compose", () => SimulateKey(ConsoleKey.N, ctrl: true));
            AddToolbarButton("\u21bb Sync", () => SimulateKey(ConsoleKey.F5));
            AddToolbarButton("\u2315 Search", () => SimulateKey(ConsoleKey.S, ctrl: true));

            if (!isDashboard && hasMessage)
            {
                _toolbar.AddItem(new SeparatorControl());
                AddToolbarButton("\u21a9 Reply", () => SimulateKey(ConsoleKey.R, ctrl: true));
                AddToolbarButton("\u21aa Forward", () => SimulateKey(ConsoleKey.F, ctrl: true));
                _toolbar.AddItem(new SeparatorControl());
                var msg = GetSelectedMessage();
                var flagLabel = msg?.IsFlagged == true ? "\u2691 Unflag" : "\u2691 Flag";
                AddToolbarButton(flagLabel, () => SimulateKey(ConsoleKey.D, ctrl: true));
                var readLabel = msg?.IsRead == true ? "\u2022 Unread" : "\u2022 Read";
                AddToolbarButton(readLabel, () => SimulateKey(ConsoleKey.U, ctrl: true));
                AddToolbarButton("\u2192 Move", () => SimulateKey(ConsoleKey.M, ctrl: true));
                AddToolbarButton("\u2717 Delete", () => SimulateKey(ConsoleKey.Delete));
            }

            _toolbar.AddItem(new SeparatorControl());
            var layoutLabel = _currentLayout == "classic" ? "\u25eb Wide" : "\u2b12 Classic";
            AddToolbarButton(layoutLabel, () => SimulateKey(ConsoleKey.F8));
            AddToolbarButton("\u2699 Settings", () => SimulateKey(ConsoleKey.OemComma, ctrl: true));
        }
    }

    private void UpdateHelpBar()
    {
        _helpBar.Clear();

        var hasMessage = GetSelectedMessage() != null;
        var checkedCount = GetCheckedCount();

        if (checkedCount > 0)
        {
            // Bulk mode — actions apply to checked messages
            _helpBar.Add("Space", "Toggle");
            _helpBar.Add("Del", $"Delete ({checkedCount})", () => SimulateKey(ConsoleKey.Delete));
            _helpBar.Add("Ctrl+U", $"Read ({checkedCount})", () => SimulateKey(ConsoleKey.U, ctrl: true));
            _helpBar.Add("Ctrl+D", $"Flag ({checkedCount})", () => SimulateKey(ConsoleKey.D, ctrl: true));
            _helpBar.Add("Ctrl+M", $"Move ({checkedCount})", () => SimulateKey(ConsoleKey.M, ctrl: true));
            _helpBar.Add("Ctrl+F", $"Forward ({checkedCount})", () => SimulateKey(ConsoleKey.F, ctrl: true));
            _helpBar.Add("Esc", "Clear", () => ClearSelection());
        }
        else
        {
            // Normal mode
            _helpBar.Add("\u2191\u2193", "Navigate");
            _helpBar.Add("Ctrl+N", "Compose", () => SimulateKey(ConsoleKey.N, ctrl: true));

            if (hasMessage)
            {
                _helpBar.Add("Ctrl+R", "Reply", () => SimulateKey(ConsoleKey.R, ctrl: true));
                _helpBar.Add("Ctrl+F", "Forward", () => SimulateKey(ConsoleKey.F, ctrl: true));
                var msg = GetSelectedMessage();
                var readLabel = msg?.IsRead == true ? "Unread" : "Read";
                var flagLabel = msg?.IsFlagged == true ? "Unflag" : "Flag";
                _helpBar.Add("Ctrl+U", readLabel, () => SimulateKey(ConsoleKey.U, ctrl: true));
                _helpBar.Add("Ctrl+D", flagLabel, () => SimulateKey(ConsoleKey.D, ctrl: true));
                _helpBar.Add("Del", "Delete", () => SimulateKey(ConsoleKey.Delete));
                _helpBar.Add("Ctrl+M", "Move", () => SimulateKey(ConsoleKey.M, ctrl: true));
            }

            _helpBar.Add("Ctrl+S", "Search", () => SimulateKey(ConsoleKey.S, ctrl: true));
            _helpBar.Add("F5", "Sync All", () => SimulateKey(ConsoleKey.F5));
            if (!_isSearchActive && _dashboardPanel?.Visible != true)
                _helpBar.Add("Shift+F5", "Sync Folder", () => SimulateKey(ConsoleKey.F5, shift: true));
            _helpBar.Add("Ctrl+,", "Settings", () => SimulateKey(ConsoleKey.OemComma, ctrl: true));
        }

        _statusBar.UpdateHelpBar(_helpBar.Render());
    }

    private void AddToolbarButton(string text, Action onClick)
    {
        var button = Controls.Button()
            .WithText(text)
            .WithBorder(ButtonBorderStyle.Rounded)
            .WithBackgroundColor(Color.Transparent)
            .WithBorderBackgroundColor(Color.Transparent)
            .OnClick((_, _) => onClick())
            .Build();
        _toolbar!.AddItem(button);
    }

    private void SetRightPanelHeader(string text, string? clearAction = null, bool showSyncAction = false,
        bool showFlaggedFilter = false)
    {
        if (_rightPanelHeader == null) return;
        _rightPanelHeader.ClearAll();
        _rightPanelHeader.AddLeftText(text);
        if (clearAction != null)
        {
            _rightPanelHeader.AddLeftSeparator();
            _rightPanelHeader.AddLeftText($"[{ColorScheme.PrimaryMarkup}]\u2715 {clearAction}[/]", () => ClearSearch());
        }
        if (showSyncAction)
        {
            _rightPanelHeader.AddLeftSeparator();
            _rightPanelHeader.AddLeftText($"[{ColorScheme.PrimaryMarkup}]\u21bb Sync[/] [grey50](Shift+F5)[/]", () => SyncActiveFolder());
        }
        if (showFlaggedFilter)
        {
            _rightPanelHeader.AddLeftSeparator();
            var starLabel = _isFlaggedFilterActive
                ? $"[yellow]\u2605 Starred[/]"
                : $"[{ColorScheme.PrimaryMarkup}]\u2606 Starred[/]";
            _rightPanelHeader.AddLeftText(starLabel, ToggleFlaggedFilter);
        }
    }

    private void SimulateKey(ConsoleKey key, bool ctrl = false, bool shift = false)
    {
        var keyInfo = new ConsoleKeyInfo('\0', key, shift, false, ctrl);
        var args = new KeyPressedEventArgs(keyInfo, false);
        OnKeyPressed(this, args);
    }

    private Components.DashboardActions GetDashboardActions() => new(
        OnCompose: () => SimulateKey(ConsoleKey.N, ctrl: true),
        OnSearch: () => SimulateKey(ConsoleKey.S, ctrl: true),
        OnSync: () => SimulateKey(ConsoleKey.F5),
        OnSettings: () => SimulateKey(ConsoleKey.OemComma, ctrl: true),
        OnAccountClick: accountId => NavigateToAccount(accountId),
        OnFolderClick: folderId => NavigateToFolder(folderId)
    );

    public void ShowError(string message) => _messageBar?.ShowError(message);

    public void ShowSuccess(string message) => _messageBar?.ShowSuccess(message);

    public void ShowInfo(string message) => _messageBar?.ShowInfo(message);

    public void ShowWarning(string message) => _messageBar?.ShowWarning(message);

    public string? ShowPersistent(string message, MessageSeverity severity = MessageSeverity.Info) =>
        _messageBar?.ShowPersistent(message, severity);

    public string? ShowProgress(string message) => _messageBar?.ShowProgress(message);

    public string? ReplaceMessage(string id, string text, MessageSeverity severity = MessageSeverity.Info,
        int? timeoutSeconds = null, bool dismissable = false) =>
        _messageBar?.Replace(id, text, severity, timeoutSeconds, dismissable);

    public void DismissMessage(string id) => _messageBar?.Dismiss(id);

    public void ShowUndoNotification(string id, string text, Action onUndo) =>
        _messageBar?.ShowWithUndo(id, text, onUndo);
}
