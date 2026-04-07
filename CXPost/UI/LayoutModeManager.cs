namespace CXPost.UI;

/// <summary>
/// Manages two independent layout states:
/// 1. Folder tree visibility (F2 toggle)
/// 2. Read mode (expands reading pane, narrows message list to strip)
/// These compose freely.
/// </summary>
public class LayoutModeManager
{
    // ── Folder tree toggle ──────────────────────────────────────────────
    public bool IsFolderTreeHidden { get; private set; }
    private int _savedFolderWidth = 28;

    public void SaveFolderWidth(int width)
    {
        if (width > 0) _savedFolderWidth = width;
    }

    public int GetSavedFolderWidth() => _savedFolderWidth;

    // ── Preview panel toggle ────────────────────────────────────────────
    public bool IsPreviewHidden { get; private set; }
    private int _savedPreviewColumnWidth;

    public void TogglePreview() => IsPreviewHidden = !IsPreviewHidden;

    public void SavePreviewColumnWidth(int width)
    {
        if (width > 0) _savedPreviewColumnWidth = width;
    }

    public int GetSavedPreviewColumnWidth() => _savedPreviewColumnWidth;

    // ── Read mode ───────────────────────────────────────────────────────
    public bool IsReadMode { get; private set; }
    public bool IsStripVisible { get; private set; } = true;
    private int _savedMessageColumnWidth;
    private bool _folderTreeHiddenBeforeReadMode;
    private bool _folderTreeChangedInReadMode;

    public const int StripWidth = 20;

    public void EnterReadMode()
    {
        // Save folder state and auto-hide for reading space
        _folderTreeHiddenBeforeReadMode = IsFolderTreeHidden;
        _folderTreeChangedInReadMode = false;
        IsFolderTreeHidden = true;
        IsReadMode = true;
        // IsStripVisible retains its last value — remembers user preference
    }

    public void ExitReadMode()
    {
        IsReadMode = false;
        // Restore folder tree state unless user manually changed it in read mode
        if (!_folderTreeChangedInReadMode)
            IsFolderTreeHidden = _folderTreeHiddenBeforeReadMode;
        // IsStripVisible retains its value for next entry
    }

    /// <summary>
    /// Toggle folder tree. When in read mode, marks as manually changed
    /// so ExitReadMode won't override the user's choice.
    /// </summary>
    public void ToggleFolderTree()
    {
        IsFolderTreeHidden = !IsFolderTreeHidden;
        if (IsReadMode) _folderTreeChangedInReadMode = true;
    }

    public void ToggleStrip() => IsStripVisible = !IsStripVisible;

    public void SaveMessageColumnWidth(int width)
    {
        if (width > 0) _savedMessageColumnWidth = width;
    }

    public int GetSavedMessageColumnWidth() => _savedMessageColumnWidth;
}
