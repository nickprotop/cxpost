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
    private void ShowMessageListView()
    {
        // Ensure message list + reading pane are visible, dashboard hidden
        if (_messageTable != null) _messageTable.Visible = true;
        if (_readingPane != null) _readingPane.Visible = true;
        if (_dashboardPanel != null) _dashboardPanel.Visible = false;
        if (_previewPanelHeader != null) _previewPanelHeader.Visible = true;
        if (_previewColumn != null) _previewColumn.Visible = true;
        if (_previewSplitter != null) _previewSplitter.Visible = true;
        UpdatePreviewHeader(GetSelectedMessage());

        if (_currentLayout == "classic")
        {
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = true;
        }
        else
        {
            if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
        }
    }

    private void ApplyDashboardVisibility()
    {
        if (_messageTable != null) _messageTable.Visible = false;
        if (_readingPane != null) _readingPane.Visible = false;
        if (_listReadingSplitter != null) _listReadingSplitter.Visible = false;
        if (_previewPanelHeader != null) _previewPanelHeader.Visible = false;
        if (_previewColumn != null) _previewColumn.Visible = false;
        if (_previewSplitter != null) _previewSplitter.Visible = false;
        if (_dashboardPanel != null) _dashboardPanel.Visible = true;
        UpdatePreviewHeader();
    }

    private void ShowDashboardView(List<IWindowControl> dashboardControls)
    {
        if (_dashboardPanel == null) return;
        _dashboardPanel.ClearContents();
        foreach (var control in dashboardControls)
            _dashboardPanel.AddControl(control);

        ApplyDashboardVisibility();

        // Keep focus on folder tree
        _mainWindow?.FocusManager?.SetFocus(_folderTree as IFocusableControl, FocusReason.Programmatic);
    }
}
