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
    private void ClearSearch()
    {
        if (!_isSearchActive) return;
        _isSearchActive = false;
        _activeSearchQuery = null;

        // Restore the folder's full message list
        _messageListCoordinator.RefreshMessageList();

        // Restore header
        var folder = _messageListCoordinator.CurrentFolder;
        if (folder != null)
        {
            var messages = _cacheService.GetMessages(folder.Id);
            SetRightPanelHeader($"[grey70]Messages[/] [grey50]({messages.Count})[/]", showSyncAction: true, showFlaggedFilter: true);
        }
        else
        {
            SetRightPanelHeader("[grey70]Messages[/]");
        }

        ClearReadingPane();
        UpdateHelpBar();
        UpdateToolbar();
    }

    private void ToggleFlaggedFilter()
    {
        _isFlaggedFilterActive = !_isFlaggedFilterActive;
        _messageListCoordinator.RefreshMessageList();
        RefreshFlaggedHeader();
    }

    private void RefreshFlaggedHeader()
    {
        List<MailMessage> allMessages;
        if (_isAggregatedView && _aggregatedFolderIds != null)
        {
            allMessages = [];
            foreach (var fid in _aggregatedFolderIds)
                allMessages.AddRange(_cacheService.GetMessages(fid));
        }
        else
        {
            var folder = _messageListCoordinator.CurrentFolder;
            if (folder == null) return;
            allMessages = _cacheService.GetMessages(folder.Id);
        }

        if (_isFlaggedFilterActive)
        {
            var starredCount = allMessages.Count(m => m.IsFlagged);
            SetRightPanelHeader(
                $"[grey70]Messages[/] [grey50]({starredCount} starred of {allMessages.Count})[/]",
                showSyncAction: true, showFlaggedFilter: true);
        }
        else
        {
            SetRightPanelHeader(
                $"[grey70]Messages[/] [grey50]({allMessages.Count})[/]",
                showSyncAction: true, showFlaggedFilter: true);
        }
    }
}
