namespace CXPost.UI;

public enum LayoutMode { Compact, Focus, Triage }

public class LayoutModeManager
{
    public LayoutMode CurrentMode { get; private set; } = LayoutMode.Compact;
    public LayoutMode? PreviousMode { get; private set; }

    // Saved Compact column widths for restoration
    private int _savedFolderWidth = 28;
    private int _savedMessageWidth;
    private int _savedPreviewWidth;

    // Focus mode column targets
    public int FocusFolderWidth => 0;       // hidden
    public int FocusStripWidth => 140;      // narrow message strip

    // Triage mode column targets
    public int TriageFolderWidth => 80;     // narrow folder strip
    public int TriagePreviewWidth => 0;     // hidden

    public void ToggleFocus()
    {
        if (CurrentMode == LayoutMode.Focus)
            GoBack();
        else
        {
            PreviousMode = CurrentMode;
            CurrentMode = LayoutMode.Focus;
        }
    }

    public void ToggleTriage()
    {
        if (CurrentMode == LayoutMode.Triage)
            GoBack();
        else
        {
            PreviousMode = CurrentMode;
            CurrentMode = LayoutMode.Triage;
        }
    }

    /// <summary>Enter Focus from Triage (e.g. pressing Enter on a message in Triage).</summary>
    public void EnterFocusFromTriage()
    {
        PreviousMode = LayoutMode.Triage;
        CurrentMode = LayoutMode.Focus;
    }

    public void GoBack()
    {
        CurrentMode = PreviousMode ?? LayoutMode.Compact;
        PreviousMode = null;
    }

    public void SaveCompactWidths(int folderWidth, int messageWidth, int previewWidth)
    {
        _savedFolderWidth = folderWidth;
        _savedMessageWidth = messageWidth;
        _savedPreviewWidth = previewWidth;
    }

    public (int folder, int message, int preview) GetSavedCompactWidths() =>
        (_savedFolderWidth, _savedMessageWidth, _savedPreviewWidth);

    /// <summary>Get target column widths for the current mode. null = fill available space.</summary>
    public (int? folder, int? message, int? preview) GetTargetWidths()
    {
        return CurrentMode switch
        {
            LayoutMode.Focus => (FocusFolderWidth, FocusStripWidth, null),
            LayoutMode.Triage => (TriageFolderWidth, null, TriagePreviewWidth),
            _ => (_savedFolderWidth,
                  _savedMessageWidth > 0 ? _savedMessageWidth : null,
                  _savedPreviewWidth > 0 ? _savedPreviewWidth : null)
        };
    }
}
