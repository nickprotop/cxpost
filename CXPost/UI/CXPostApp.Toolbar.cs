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
            AddToolbarButton($"\u2717 Delete ({checkedCount}) [{ColorScheme.MutedMarkup}]Del[/]", () => SimulateKey(ConsoleKey.Delete));
            AddToolbarButton($"\u2022 Read ({checkedCount}) [{ColorScheme.MutedMarkup}]^U[/]", () => SimulateKey(ConsoleKey.U, ctrl: true));
            AddToolbarButton($"\u2691 Flag ({checkedCount}) [{ColorScheme.MutedMarkup}]^D[/]", () => SimulateKey(ConsoleKey.D, ctrl: true));
            AddToolbarButton($"\u2192 Move ({checkedCount}) [{ColorScheme.MutedMarkup}]^M[/]", () => SimulateKey(ConsoleKey.M, ctrl: true));
            AddToolbarButton($"\u21aa Fwd ({checkedCount}) [{ColorScheme.MutedMarkup}]^F[/]", () => SimulateKey(ConsoleKey.F, ctrl: true));
            _toolbar.AddItem(new SeparatorControl());
            AddToolbarButton("\u2715 Clear", () => ClearSelection());
        }
        else
        {
            // Normal toolbar
            AddToolbarButton($"\u2709 Compose [{ColorScheme.MutedMarkup}]^N[/]", () => SimulateKey(ConsoleKey.N, ctrl: true));
            AddToolbarButton($"\u21bb Sync [{ColorScheme.MutedMarkup}]F5[/]", () => SimulateKey(ConsoleKey.F5));
            AddToolbarButton($"\u2315 Search [{ColorScheme.MutedMarkup}]^S[/]", () => SimulateKey(ConsoleKey.S, ctrl: true));

            if (!isDashboard && hasMessage)
            {
                _toolbar.AddItem(new SeparatorControl());
                AddToolbarButton($"\u21a9 Reply [{ColorScheme.MutedMarkup}]^R[/]", () => SimulateKey(ConsoleKey.R, ctrl: true));
                AddToolbarButton($"\u21aa Fwd [{ColorScheme.MutedMarkup}]^F[/]", () => SimulateKey(ConsoleKey.F, ctrl: true));
                _toolbar.AddItem(new SeparatorControl());
                var msg = GetSelectedMessage();
                var flagLabel = msg?.IsFlagged == true ? "\u2691 Unflag" : "\u2691 Flag";
                AddToolbarButton($"{flagLabel} [{ColorScheme.MutedMarkup}]^D[/]", () => SimulateKey(ConsoleKey.D, ctrl: true));
                var readLabel = msg?.IsRead == true ? "\u2022 Unread" : "\u2022 Read";
                AddToolbarButton($"{readLabel} [{ColorScheme.MutedMarkup}]^U[/]", () => SimulateKey(ConsoleKey.U, ctrl: true));
                AddToolbarButton($"\u2192 Move [{ColorScheme.MutedMarkup}]^M[/]", () => SimulateKey(ConsoleKey.M, ctrl: true));
                AddToolbarButton($"\u2717 Delete [{ColorScheme.MutedMarkup}]Del[/]", () => SimulateKey(ConsoleKey.Delete));
            }

            _toolbar.AddItem(new SeparatorControl());
            AddToolbarButton($"\u2699 Settings [{ColorScheme.MutedMarkup}]^,[/]", () => SimulateKey(ConsoleKey.OemComma, ctrl: true));
        }
    }

    private void UpdateBottomBar()
    {
        if (_bottomBar == null) return;

        _bottomBar.BatchUpdate(() =>
        {
            _bottomBar.ClearAll();

            var hasMessage = GetSelectedMessage() != null;
            var checkedCount = GetCheckedCount();
            var isDashboard = _dashboardPanel?.Visible == true;

            // ── Left side: contextual hints ──────────────────────────────────
            if (_layoutModeManager.IsReadMode)
            {
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Esc[/][grey70]:Back[/]");
                _bottomBar.AddLeftSeparator();
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]j/k[/][grey70]:Next/Prev[/]");
            }
            else if (checkedCount > 0)
            {
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Del[/][grey70]:Delete {checkedCount}[/]",
                    () => SimulateKey(ConsoleKey.Delete));
                _bottomBar.AddLeftSeparator();
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Ctrl+M[/][grey70]:Move[/]",
                    () => SimulateKey(ConsoleKey.M, ctrl: true));
                _bottomBar.AddLeftSeparator();
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Esc[/][grey70]:Clear[/]",
                    () => ClearSelection());
            }
            else if (isDashboard && !hasMessage)
            {
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Enter[/][grey70]:Open folder[/]");
                _bottomBar.AddLeftSeparator();
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]F5[/][grey70]:Sync all[/]",
                    () => SimulateKey(ConsoleKey.F5));
            }
            else
            {
                if (hasMessage)
                {
                    _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Enter[/][grey70]:Read view[/]",
                        () => EnterReadMode());
                    _bottomBar.AddLeftSeparator();
                    _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]Space[/][grey70]:Check[/]");
                    _bottomBar.AddLeftSeparator();
                }
                _bottomBar.AddLeftText($"[{ColorScheme.PrimaryMarkup}]j/k[/][grey70]:Next/Prev[/]");
            }

            // ── Right side: view toggles ─────────────────────────────────────
            var foldersColor = _layoutModeManager.IsFolderTreeHidden ? ColorScheme.MutedMarkup : ColorScheme.PrimaryMarkup;
            _bottomBar.AddRightText($"[{foldersColor}]Folders[/] [{ColorScheme.MutedMarkup}]F2[/]",
                () => ToggleFolderTree());

            if (!_layoutModeManager.IsReadMode && !isDashboard)
            {
                _bottomBar.AddRightSeparator();
                var previewColor = _layoutModeManager.IsPreviewHidden ? ColorScheme.MutedMarkup : ColorScheme.PrimaryMarkup;
                _bottomBar.AddRightText($"[{previewColor}]Preview[/] [{ColorScheme.MutedMarkup}]F3[/]",
                    () => TogglePreview());
            }

            // Read and Layout toggles (not available on dashboard)
            if (!isDashboard)
            {
                _bottomBar.AddRightSeparator();
                var readColor = _layoutModeManager.IsReadMode ? ColorScheme.PrimaryMarkup : ColorScheme.MutedMarkup;
                _bottomBar.AddRightText($"[{readColor}]Read[/] [{ColorScheme.MutedMarkup}]F4[/]",
                    () => { if (_layoutModeManager.IsReadMode) ExitReadMode(); else EnterReadMode(); });

                // Strip toggle (only in read mode)
                if (_layoutModeManager.IsReadMode)
                {
                    _bottomBar.AddRightSeparator();
                    var stripColor = _layoutModeManager.IsStripVisible ? ColorScheme.PrimaryMarkup : ColorScheme.MutedMarkup;
                    _bottomBar.AddRightText($"[{stripColor}]List[/] [{ColorScheme.MutedMarkup}]^B[/]",
                        () => ToggleReadStrip());
                }

                // Layout toggle (hidden in read mode — read mode has its own grid)
                if (!_layoutModeManager.IsReadMode)
                {
                    _bottomBar.AddRightSeparator();
                    var layoutName = _currentLayout == "classic" ? "Wide" : "Classic";
                    _bottomBar.AddRightText($"[{ColorScheme.MutedMarkup}]{layoutName}[/] [{ColorScheme.MutedMarkup}]F8[/]",
                        () => SimulateKey(ConsoleKey.F8));
                }
            }
        });
    }

    private void AddToolbarButton(string text, Action onClick)
    {
        var button = Controls.Button()
            .WithText(text)
            .WithBorder(ButtonBorderStyle.None)
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

    public void StartSyncProgress() => _messageBar?.StartSyncProgress();
    public void SetSyncProgress(int fetched, int expected) => _messageBar?.SetSyncProgress(fetched, expected);
    public void EndSyncProgress() => _messageBar?.EndSyncProgress();
}
