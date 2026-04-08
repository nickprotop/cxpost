using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Windows;
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

            if (hide)
            {
                // Fade out the reading pane area before hiding
                if (_readingPane != null && _mainWindow != null)
                {
                    float fadeOut = 0f;
                    WindowRenderer.BufferPaintDelegate? fadeHandler = null;
                    fadeHandler = (buffer, dirtyRegion, clipRect) =>
                    {
                        if (fadeOut <= 0.01f || _readingPane == null) return;
                        var paneRect = new LayoutRect(
                            _readingPane.ActualX, _readingPane.ActualY,
                            _readingPane.ActualWidth, _readingPane.ActualHeight);
                        ColorBlendHelper.ApplyColorOverlay(buffer, Color.Black, fadeOut, 0.5f, paneRect);
                    };
                    _mainWindow.PostBufferPaint += fadeHandler;
                    _ws.Animations.Animate(
                        from: 0f, to: 0.4f,
                        duration: TimeSpan.FromMilliseconds(150),
                        easing: EasingFunctions.EaseOut,
                        onUpdate: t =>
                        {
                            fadeOut = t;
                            _mainWindow?.Invalidate(redrawAll: true);
                        },
                        onComplete: () =>
                        {
                            _mainWindow!.PostBufferPaint -= fadeHandler;
                            if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
                            if (_previewPanelHeader != null) _previewPanelHeader.Visible = false;
                            if (_readingPane != null) _readingPane.Visible = false;
                        });
                }
            }
            else
            {
                if (_listReadingSplitter != null) _listReadingSplitter.Visible = true;
                if (_previewPanelHeader != null) _previewPanelHeader.Visible = true;
                if (_readingPane != null) _readingPane.Visible = true;
                TriggerReadingPaneFadeIn();
            }
        }


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
        // Ensure reading pane is visible — it may have been hidden by preview toggle
        if (_readingPane != null) _readingPane.Visible = true;
        if (_previewPanelHeader != null) _previewPanelHeader.Visible = true;
        PopulateReadModeStrip();
        RebuildMainGrid();
        TriggerReadingPaneFadeIn();


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

    /// <summary>
    /// Saves current grid column widths to LayoutModeManager and config.
    /// Call before any operation that rebuilds the grid.
    /// </summary>
    private void SaveCurrentGridWidths()
    {
        if (_mainGrid == null || _layoutModeManager.IsReadMode) return;

        var columns = _mainGrid.Columns;
        if (columns.Count == 0) return;

        // Column[0] is always the folder column in normal mode
        if (!_layoutModeManager.IsFolderTreeHidden)
            _layoutModeManager.SaveFolderWidth(columns[0].Width ?? 28);

        if (_currentLayout == "wide")
        {
            if (columns.Count > 1)
                _layoutModeManager.SaveMessageColumnWidth(columns[1].Width ?? 0);
            if (columns.Count > 2)
                _layoutModeManager.SavePreviewColumnWidth(columns[2].Width ?? 0);
        }
    }

    /// <summary>
    /// Persists layout widths to config for next session.
    /// </summary>
    private void PersistLayoutWidths()
    {
        _config.FolderColumnWidth = _layoutModeManager.GetSavedFolderWidth();
        _config.MessageColumnWidth = _layoutModeManager.GetSavedMessageColumnWidth();
        _config.PreviewColumnWidth = _layoutModeManager.GetSavedPreviewColumnWidth();
        _config.PreviewHidden = _layoutModeManager.IsPreviewHidden;
        _configService.Save(_config);
    }
}
