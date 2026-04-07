using SharpConsoleUI.Animation;

namespace CXPost.UI;

public partial class CXPostApp
{
    private void ToggleFolderTree()
    {
        if (_mainGrid == null) return;

        var columns = _mainGrid.Columns;
        if (columns.Count == 0) return;

        var duration = TimeSpan.FromMilliseconds(250);

        if (!_layoutModeManager.IsFolderTreeHidden)
        {
            _layoutModeManager.SaveFolderWidth(columns[0].Width ?? 28);
            _layoutModeManager.ToggleFolderTree();
            _mainGrid.AnimateColumnWidth(0, 0, duration);
        }
        else
        {
            _layoutModeManager.ToggleFolderTree();
            _mainGrid.AnimateColumnWidth(0, _layoutModeManager.GetSavedFolderWidth(), duration);
        }

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateHelpBar();
    }

    private void EnterReadMode()
    {
        if (_mainGrid == null || _layoutModeManager.IsReadMode) return;

        var columns = _mainGrid.Columns;
        if (columns.Count < 2) return;

        _layoutModeManager.SaveMessageColumnWidth(columns[1].Width ?? 0);
        _layoutModeManager.EnterReadMode();

        var duration = TimeSpan.FromMilliseconds(250);
        _mainGrid.AnimateColumnWidth(1, LayoutModeManager.StripWidth, duration);

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateHelpBar();
    }

    private void ExitReadMode()
    {
        if (_mainGrid == null || !_layoutModeManager.IsReadMode) return;

        _layoutModeManager.ExitReadMode();

        var columns = _mainGrid.Columns;
        if (columns.Count < 2) return;

        var savedWidth = _layoutModeManager.GetSavedMessageColumnWidth();
        var duration = TimeSpan.FromMilliseconds(250);

        if (savedWidth > 0)
        {
            _mainGrid.AnimateColumnWidth(1, savedWidth, duration);
        }
        else
        {
            // Restore fill behavior
            columns[1].Width = null;
        }

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateHelpBar();
    }

    private void ToggleReadStrip()
    {
        if (_mainGrid == null || !_layoutModeManager.IsReadMode) return;

        _layoutModeManager.ToggleStrip();

        var columns = _mainGrid.Columns;
        if (columns.Count < 2) return;

        var duration = TimeSpan.FromMilliseconds(200);

        if (_layoutModeManager.IsStripVisible)
            _mainGrid.AnimateColumnWidth(1, LayoutModeManager.StripWidth, duration);
        else
            _mainGrid.AnimateColumnWidth(1, 0, duration);

        UpdateFocusDimmingPanes();
    }
}
