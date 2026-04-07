using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using CXPost.UI.Components;

namespace CXPost.UI;

public partial class CXPostApp
{
    private FocusDimming? _focusDimming;

    private void InitFocusDimming()
    {
        _focusDimming = new FocusDimming
        {
            BackgroundDimIntensity = 0.22f,
            ForegroundDimIntensity = 0.12f,
            ShadowEdgeWidth = 2,
            ShadowExtraIntensity = 0.15f,
            AccentColor = ColorScheme.ActiveBorderColor,
            AccentOpacity = 0.8f,
            TransitionDuration = TimeSpan.FromMilliseconds(200),
            TransitionEasing = EasingFunctions.EaseOut
        };

        UpdateFocusDimmingPanes();

        _mainWindow!.PostBufferPaint += FocusDimmingOverlay;
        _mainWindow.FocusManager.FocusChanged += OnFocusChangedForDimming;
    }

    private void UpdateFocusDimmingPanes()
    {
        if (_focusDimming == null || _mainGrid == null) return;

        _focusDimming.ClearPanes();

        var columns = _mainGrid.Columns;
        string[] paneIds = ["folders", "messages", "preview"];

        for (int i = 0; i < columns.Count && i < paneIds.Length; i++)
        {
            var col = columns[i];
            if (!col.Visible || col.ActualWidth <= 0) continue;

            var bounds = new LayoutRect(col.ActualX, col.ActualY, col.ActualWidth, col.ActualHeight);
            _focusDimming.RegisterPane(new PaneRegistration(paneIds[i], bounds));
        }

        // Set initial active pane based on current focus
        var focused = _mainWindow?.FocusManager.FocusedControl;
        var activePaneId = ResolvePaneId(focused);
        if (activePaneId != null)
            _focusDimming.SetActivePane(activePaneId);
    }

    private void OnFocusChangedForDimming(object? sender, FocusChangedEventArgs e)
    {
        if (_focusDimming == null) return;

        var newPaneId = ResolvePaneId(e.Current);
        if (newPaneId == null || newPaneId == _focusDimming.ActivePaneId) return;

        RefreshFocusDimmingBounds();
        _focusDimming.SetActivePaneAnimated(newPaneId);
        _focusDimming.AnimateTransition(_ws.Animations, onFrame: () =>
        {
            _mainWindow?.Invalidate(redrawAll: true);
        });
    }

    private void RefreshFocusDimmingBounds()
    {
        if (_focusDimming == null || _mainGrid == null) return;

        var columns = _mainGrid.Columns;
        string[] paneIds = ["folders", "messages", "preview"];

        for (int i = 0; i < columns.Count && i < paneIds.Length; i++)
        {
            var col = columns[i];
            if (!col.Visible || col.ActualWidth <= 0) continue;

            var bounds = new LayoutRect(col.ActualX, col.ActualY, col.ActualWidth, col.ActualHeight);
            _focusDimming.UpdatePaneBounds(paneIds[i], bounds);
        }
    }

    private string? ResolvePaneId(IFocusableControl? control)
    {
        if (control == null) return null;
        if (ReferenceEquals(control, _folderTree)) return "folders";
        if (ReferenceEquals(control, _messageTable)) return "messages";
        if (ReferenceEquals(control, _readingPane)) return "preview";
        return null;
    }

    private void FocusDimmingOverlay(CharacterBuffer buffer, LayoutRect dirtyRegion, LayoutRect clipRect)
    {
        if (_focusDimming == null || _focusDimming.Suspended) return;

        RefreshFocusDimmingBounds();
        _focusDimming.ApplyOverlays(buffer);

        // Apply splitter accents for columns after the first
        if (_mainGrid == null) return;
        var columns = _mainGrid.Columns;
        for (int i = 1; i < columns.Count; i++)
        {
            var col = columns[i];
            if (!col.Visible || col.ActualWidth <= 0) continue;

            int splitterX = col.ActualX - 1;
            _focusDimming.ApplySplitterAccent(buffer, splitterX, col.ActualY, col.ActualHeight);
        }
    }

    public void SuspendFocusDimming()
    {
        if (_focusDimming != null)
            _focusDimming.Suspended = true;
    }

    public void ResumeFocusDimming()
    {
        if (_focusDimming != null)
            _focusDimming.Suspended = false;
    }
}
