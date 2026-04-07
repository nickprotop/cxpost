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

    public void ToggleFolderTree() => IsFolderTreeHidden = !IsFolderTreeHidden;

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

    public const int StripWidth = 30;

    public void EnterReadMode()
    {
        IsReadMode = true;
        IsStripVisible = true;
    }

    public void ExitReadMode()
    {
        IsReadMode = false;
        IsStripVisible = true;
    }

    public void ToggleStrip() => IsStripVisible = !IsStripVisible;

    public void SaveMessageColumnWidth(int width)
    {
        if (width > 0) _savedMessageColumnWidth = width;
    }

    public int GetSavedMessageColumnWidth() => _savedMessageColumnWidth;
}
