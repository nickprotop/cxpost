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
        mgr.EnterReadMode();
        mgr.ToggleStrip();
        Assert.False(mgr.IsStripVisible);
    }

    [Fact]
    public void ToggleStrip_Twice_ShowsStrip()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        mgr.ToggleStrip();
        mgr.ToggleStrip();
        Assert.True(mgr.IsStripVisible);
    }

    [Fact]
    public void ExitReadMode_ResetsStripVisibility()
    {
        var mgr = new LayoutModeManager();
        mgr.EnterReadMode();
        mgr.ToggleStrip();
        mgr.ExitReadMode();
        mgr.EnterReadMode();
        Assert.True(mgr.IsStripVisible);
    }

    [Fact]
    public void SaveMessageColumnWidth_PreservesWidth()
    {
        var mgr = new LayoutModeManager();
        mgr.SaveMessageColumnWidth(200);
        Assert.Equal(200, mgr.GetSavedMessageColumnWidth());
    }

    [Fact]
    public void TreeAndReadMode_Independent()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFolderTree();
        mgr.EnterReadMode();
        Assert.True(mgr.IsFolderTreeHidden);
        Assert.True(mgr.IsReadMode);
        mgr.ExitReadMode();
        Assert.True(mgr.IsFolderTreeHidden);
        Assert.False(mgr.IsReadMode);
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
    public void AllToggles_Independent()
    {
        var mgr = new LayoutModeManager();
        mgr.ToggleFolderTree();
        mgr.TogglePreview();
        mgr.EnterReadMode();
        Assert.True(mgr.IsFolderTreeHidden);
        Assert.True(mgr.IsPreviewHidden);
        Assert.True(mgr.IsReadMode);
        mgr.TogglePreview();
        Assert.True(mgr.IsFolderTreeHidden);
        Assert.False(mgr.IsPreviewHidden);
        Assert.True(mgr.IsReadMode);
    }
}
