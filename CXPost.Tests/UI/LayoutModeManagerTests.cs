using CXPost.UI;

namespace CXPost.Tests.UI;

public class LayoutModeManagerTests
{
    [Fact]
    public void Initial_FolderTreeVisible()
    {
        var mgr = new LayoutModeManager();
        Assert.False(mgr.IsFolderTreeHidden);
    }

    [Fact]
    public void ToggleFolderTree_HidesTree()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFolderTree();
        Assert.True(mgr.IsFolderTreeHidden);
    }

    [Fact]
    public void ToggleFolderTree_Twice_ShowsTree()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFolderTree();
        mgr.ToggleFolderTree();
        Assert.False(mgr.IsFolderTreeHidden);
    }

    [Fact]
    public void SaveFolderWidth_PreservesWidth()
    {
        var mgr = new LayoutModeManager();
        mgr.SaveFolderWidth(35);
        Assert.Equal(35, mgr.GetSavedFolderWidth());
    }

    [Fact]
    public void SaveFolderWidth_IgnoresZero()
    {
        var mgr = new LayoutModeManager();
        mgr.SaveFolderWidth(35);
        mgr.SaveFolderWidth(0);
        Assert.Equal(35, mgr.GetSavedFolderWidth());
    }

    [Fact]
    public void DefaultFolderWidth_Is28()
    {
        var mgr = new LayoutModeManager();
        Assert.Equal(28, mgr.GetSavedFolderWidth());
    }

    [Fact]
    public void Initial_NotInReadMode()
    {
        var mgr = new LayoutModeManager();
        Assert.False(mgr.IsReadMode);
    }

    [Fact]
    public void EnterReadMode_SetsFlag()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        Assert.True(mgr.IsReadMode);
    }

    [Fact]
    public void ExitReadMode_ClearsFlag()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        mgr.ExitReadMode();
        Assert.False(mgr.IsReadMode);
    }

    [Fact]
    public void ReadMode_StripVisibleByDefault()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        Assert.True(mgr.IsStripVisible);
    }

    [Fact]
    public void ToggleStrip_HidesStrip()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();  // strip starts visible
        mgr.ToggleStrip();    // visible → hidden
        Assert.False(mgr.IsStripVisible);
    }

    [Fact]
    public void ToggleStrip_Twice_ReturnsToVisible()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        mgr.ToggleStrip(); // visible → hidden
        mgr.ToggleStrip(); // hidden → visible
        Assert.True(mgr.IsStripVisible);
    }

    [Fact]
    public void StripVisibility_RememberedAcrossReadModeSessions()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        mgr.ToggleStrip(); // visible → hidden
        Assert.False(mgr.IsStripVisible);
        mgr.ExitReadMode();
        mgr.EnterReadMode();
        Assert.False(mgr.IsStripVisible); // remembers last preference
    }

    [Fact]
    public void SaveMessageColumnWidth_PreservesWidth()
    {
        var mgr = new LayoutModeManager();
        mgr.SaveMessageColumnWidth(200);
        Assert.Equal(200, mgr.GetSavedMessageColumnWidth());
    }

    [Fact]
    public void EnterReadMode_AutoHidesFolderTree()
    {
        var mgr = new LayoutModeManager();
        Assert.False(mgr.IsFolderTreeHidden);
        mgr.EnterReadMode();
        Assert.True(mgr.IsFolderTreeHidden); // auto-hidden
    }

    [Fact]
    public void ExitReadMode_RestoresFolderTreeState()
    {
        var mgr = new LayoutModeManager();
        Assert.False(mgr.IsFolderTreeHidden); // visible before
        mgr.EnterReadMode();
        Assert.True(mgr.IsFolderTreeHidden); // auto-hidden
        mgr.ExitReadMode();
        Assert.False(mgr.IsFolderTreeHidden); // restored to visible
    }

    [Fact]
    public void ExitReadMode_KeepsManualFolderTreeChange()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        mgr.ToggleFolderTree(); // manually show folders in read mode
        Assert.False(mgr.IsFolderTreeHidden);
        mgr.ExitReadMode();
        Assert.False(mgr.IsFolderTreeHidden); // keeps manual override
    }

    [Fact]
    public void ExitReadMode_WhenTreeWasAlreadyHidden_RestoresHidden()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFolderTree(); // hide before entering read mode
        Assert.True(mgr.IsFolderTreeHidden);
        mgr.EnterReadMode();
        Assert.True(mgr.IsFolderTreeHidden); // still hidden
        mgr.ExitReadMode();
        Assert.True(mgr.IsFolderTreeHidden); // restored to hidden
    }

    [Fact]
    public void Initial_PreviewVisible()
    {
        var mgr = new LayoutModeManager();
        Assert.False(mgr.IsPreviewHidden);
    }

    [Fact]
    public void TogglePreview_HidesPreview()
    {
        var mgr = new LayoutModeManager();
        mgr.TogglePreview();
        Assert.True(mgr.IsPreviewHidden);
    }

    [Fact]
    public void TogglePreview_Twice_ShowsPreview()
    {
        var mgr = new LayoutModeManager();
        mgr.TogglePreview();
        mgr.TogglePreview();
        Assert.False(mgr.IsPreviewHidden);
    }

    [Fact]
    public void SavePreviewColumnWidth_PreservesWidth()
    {
        var mgr = new LayoutModeManager();
        mgr.SavePreviewColumnWidth(150);
        Assert.Equal(150, mgr.GetSavedPreviewColumnWidth());
    }

    [Fact]
    public void PreviewToggle_IndependentOfReadMode()
    {
        var mgr = new LayoutModeManager();
        mgr.TogglePreview();
        Assert.True(mgr.IsPreviewHidden);
        mgr.EnterReadMode();
        Assert.True(mgr.IsPreviewHidden); // preview state preserved
        Assert.True(mgr.IsReadMode);
        mgr.TogglePreview();
        Assert.False(mgr.IsPreviewHidden);
        mgr.ExitReadMode();
        Assert.False(mgr.IsPreviewHidden); // still not hidden
    }
}
