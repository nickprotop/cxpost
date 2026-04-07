using CXPost.UI;

namespace CXPost.Tests.UI;

public class LayoutModeManagerTests
{
    [Fact]
    public void InitialMode_IsCompact()
    {
        var mgr = new LayoutModeManager();
        Assert.Equal(LayoutMode.Compact, mgr.CurrentMode);
    }

    [Fact]
    public void ToggleFocus_FromCompact_EntersFocus()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFocus();
        Assert.Equal(LayoutMode.Focus, mgr.CurrentMode);
        Assert.Equal(LayoutMode.Compact, mgr.PreviousMode);
    }

    [Fact]
    public void ToggleFocus_FromFocus_ReturnsToCompact()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFocus();
        mgr.ToggleFocus();
        Assert.Equal(LayoutMode.Compact, mgr.CurrentMode);
    }

    [Fact]
    public void ToggleTriage_FromCompact_EntersTriage()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleTriage();
        Assert.Equal(LayoutMode.Triage, mgr.CurrentMode);
        Assert.Equal(LayoutMode.Compact, mgr.PreviousMode);
    }

    [Fact]
    public void ToggleTriage_FromTriage_ReturnsToCompact()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleTriage();
        mgr.ToggleTriage();
        Assert.Equal(LayoutMode.Compact, mgr.CurrentMode);
    }

    [Fact]
    public void ToggleFocus_FromTriage_SwitchesToFocus()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleTriage();
        mgr.ToggleFocus();
        Assert.Equal(LayoutMode.Focus, mgr.CurrentMode);
        Assert.Equal(LayoutMode.Triage, mgr.PreviousMode);
    }

    [Fact]
    public void GoBack_ReturnsToCompactByDefault()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFocus();
        mgr.GoBack();
        Assert.Equal(LayoutMode.Compact, mgr.CurrentMode);
    }

    [Fact]
    public void GoBack_FromFocusEnteredViaTriage_ReturnsToTriage()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleTriage();
        mgr.EnterFocusFromTriage();
        Assert.Equal(LayoutMode.Focus, mgr.CurrentMode);
        mgr.GoBack();
        Assert.Equal(LayoutMode.Triage, mgr.CurrentMode);
    }

    [Fact]
    public void SavedWidths_PreservedAcrossTransitions()
    {
        var mgr = new LayoutModeManager();
        mgr.SaveCompactWidths(folderWidth: 28, messageWidth: 200, previewWidth: 150);
        mgr.ToggleFocus();
        mgr.GoBack();

        var (folder, message, preview) = mgr.GetSavedCompactWidths();
        Assert.Equal(28, folder);
        Assert.Equal(200, message);
        Assert.Equal(150, preview);
    }

    [Fact]
    public void GetTargetWidths_Focus_ReturnsCorrectWidths()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFocus();
        var (folder, message, preview) = mgr.GetTargetWidths();
        Assert.Equal(0, folder);
        Assert.Equal(140, message);
        Assert.Null(preview);
    }

    [Fact]
    public void GetTargetWidths_Triage_ReturnsCorrectWidths()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleTriage();
        var (folder, message, preview) = mgr.GetTargetWidths();
        Assert.Equal(80, folder);
        Assert.Null(message);
        Assert.Equal(0, preview);
    }
}
