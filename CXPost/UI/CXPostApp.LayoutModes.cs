using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Parsing;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI;

public partial class CXPostApp
{
    private void ToggleFolderTree()
    {
        if (_mainGrid == null) return;

        if (_layoutModeManager.IsReadMode)
        {
            // In read mode, toggle and rebuild grid
            var columns = _mainGrid.Columns;
            if (!_layoutModeManager.IsFolderTreeHidden && columns.Count > 0)
                _layoutModeManager.SaveFolderWidth(columns[0].Width ?? 28);
            _layoutModeManager.ToggleFolderTree();
            RebuildMainGrid();
        }
        else
        {
            // Normal mode: animate column width
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
        }

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateBottomBar();
    }

    private void TogglePreview()
    {
        if (_mainGrid == null || _layoutModeManager.IsReadMode) return;

        var columns = _mainGrid.Columns;

        if (_currentLayout == "wide")
        {
            // Wide layout: preview is its own column (last visible column)
            var previewColIdx = columns.Count - 1;
            if (previewColIdx < 1) return;
            var duration = TimeSpan.FromMilliseconds(250);

            if (!_layoutModeManager.IsPreviewHidden)
            {
                _layoutModeManager.SavePreviewColumnWidth(columns[previewColIdx].Width ?? 0);
                _layoutModeManager.TogglePreview();
                _mainGrid.AnimateColumnWidth(previewColIdx, 0, duration);
            }
            else
            {
                _layoutModeManager.TogglePreview();
                var saved = _layoutModeManager.GetSavedPreviewColumnWidth();
                _mainGrid.AnimateColumnWidth(previewColIdx, saved > 0 ? saved : 200, duration);
            }
        }
        else
        {
            // Classic layout: preview is below splitter in same column
            // Toggle visibility of splitter + preview header + reading pane
            _layoutModeManager.TogglePreview();
            var hide = _layoutModeManager.IsPreviewHidden;
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = !hide;
            if (_previewPanelHeader != null) _previewPanelHeader.Visible = !hide;
            if (_readingPane != null) _readingPane.Visible = !hide;
        }

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateBottomBar();
    }

    private void EnterReadMode()
    {
        if (_mainGrid == null || _layoutModeManager.IsReadMode) return;

        // Save widths before restructuring
        var columns = _mainGrid.Columns;
        if (columns.Count > 0)
            _layoutModeManager.SaveFolderWidth(columns[0].Width ?? 28);
        if (columns.Count > 1)
            _layoutModeManager.SaveMessageColumnWidth(columns[1].Width ?? 0);
        if (_currentLayout == "wide" && columns.Count > 2)
            _layoutModeManager.SavePreviewColumnWidth(columns[2].Width ?? 0);

        _layoutModeManager.EnterReadMode();
        PopulateReadModeStrip();
        RebuildMainGrid();
        TriggerReadingPaneFadeIn();

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateBottomBar();
        var msg = GetSelectedMessage();
        if (msg != null) UpdatePreviewHeader(msg);
        else UpdatePreviewHeader();
    }

    private void ExitReadMode()
    {
        if (_mainGrid == null || !_layoutModeManager.IsReadMode) return;

        // Sync strip selection back to message table
        if (_readModeList != null && _messageTable != null)
        {
            var stripIdx = _readModeList.SelectedIndex;
            if (stripIdx >= 0 && stripIdx < _messageTable.RowCount)
                _messageTable.SelectedRowIndex = stripIdx;
        }

        _layoutModeManager.ExitReadMode();
        RebuildMainGrid();

        UpdateFocusDimmingPanes();
        UpdateToolbar();
        UpdateBottomBar();
        var msg = GetSelectedMessage();
        if (msg != null) UpdatePreviewHeader(msg);
        else UpdatePreviewHeader();
    }

    private void ToggleReadStrip()
    {
        if (_mainGrid == null || !_layoutModeManager.IsReadMode) return;

        _layoutModeManager.ToggleStrip();

        // Find the strip column index
        var columns = _mainGrid.Columns;
        var stripColIdx = _layoutModeManager.IsFolderTreeHidden ? 0 : 1;
        if (stripColIdx >= columns.Count) return;

        var duration = TimeSpan.FromMilliseconds(200);
        if (_layoutModeManager.IsStripVisible)
            _mainGrid.AnimateColumnWidth(stripColIdx, LayoutModeManager.StripWidth, duration);
        else
            _mainGrid.AnimateColumnWidth(stripColIdx, 0, duration);

        UpdateFocusDimmingPanes();
        var msg = GetSelectedMessage();
        if (msg != null) UpdatePreviewHeader(msg);
        else UpdatePreviewHeader();
    }

    private void PopulateReadModeStrip()
    {
        if (_readModeList == null || _messageTable == null) return;

        _readModeList.ClearItems();

        for (var i = 0; i < _messageTable.RowCount; i++)
        {
            var row = _messageTable.GetRow(i);
            if (row.Tag is not MailMessage msg) continue;

            var senderName = msg.FromName ?? msg.FromAddress ?? "Unknown";
            if (senderName.Length > 25) senderName = senderName[..22] + "...";

            var textColor = msg.IsRead ? ColorScheme.ReadMarkup : ColorScheme.UnreadMarkup;
            var text = $"[{textColor}]{MarkupParser.Escape(senderName)}[/]";

            var icon = msg.IsFlagged ? "\u2605" : null;
            var iconColor = msg.IsFlagged ? (Color?)Color.Yellow : null;

            var item = new ListItem(text, icon, iconColor) { Tag = msg };
            _readModeList.AddItem(item);
        }

        // Sync selection from table
        var selectedIdx = _messageTable.SelectedRowIndex;
        if (selectedIdx >= 0 && selectedIdx < _readModeList.Items.Count)
            _readModeList.SelectedIndex = selectedIdx;
    }
}
