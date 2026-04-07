using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace CXPost.UI;

public partial class CXPostApp
{
    private void TransitionToMode(Action modeChange)
    {
        if (_mainGrid == null) return;

        var columns = _mainGrid.Columns;
        var prevMode = _layoutModeManager.CurrentMode;

        // Save current widths if leaving Compact
        if (prevMode == LayoutMode.Compact && columns.Count >= 2)
        {
            _layoutModeManager.SaveCompactWidths(
                folderWidth: columns[0].Width ?? 28,
                messageWidth: columns.Count > 1 ? columns[1].Width ?? 0 : 0,
                previewWidth: columns.Count > 2 ? columns[2].Width ?? 0 : 0);
        }

        modeChange();

        var newMode = _layoutModeManager.CurrentMode;
        var duration = TimeSpan.FromMilliseconds(250);
        var targets = _layoutModeManager.GetTargetWidths();

        // Animate column widths
        if (columns.Count > 0 && targets.folder.HasValue)
            _mainGrid.AnimateColumnWidth(0, targets.folder.Value, duration);
        if (columns.Count > 1 && targets.message.HasValue)
            _mainGrid.AnimateColumnWidth(1, targets.message.Value, duration);
        if (columns.Count > 2 && targets.preview.HasValue)
            _mainGrid.AnimateColumnWidth(2, targets.preview.Value, duration);

        // Update focus dimming pane registrations
        UpdateFocusDimmingPanes();

        // Update toolbar and help bar
        UpdateToolbar();
        UpdateHelpBar();
    }
}
