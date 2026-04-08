using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;

namespace CXPost.UI.Components;

public class ReadingPaneCompositor
{
    private readonly ScrollablePanelControl _readingPane;

    // Purple/indigo tint
    private static readonly Color HeaderTintColor = new(80, 50, 140);
    private const float HeaderTintIntensity = 0.25f;
    private const float HeaderFgRatio = 0.1f;

    // Teal-green tint
    private static readonly Color AttachmentTintColor = new(30, 140, 100);
    private const float AttachmentTintIntensity = 0.20f;
    private const float AttachmentFgRatio = 0.1f;

    private static readonly Color QuoteDimColor = new(80, 80, 100);
    private const float QuoteDimIntensity = 0.12f;
    private const float QuoteFgRatio = 0.3f;

    private const float SignatureDimIntensity = 0.55f;
    private const float SignatureFgRatio = 0.5f;

    public ReadingPaneCompositor(ScrollablePanelControl readingPane)
    {
        _readingPane = readingPane;
    }

    public void OnPaint(CharacterBuffer buffer, LayoutRect dirtyRegion, LayoutRect clipRect)
    {
        if (!_readingPane.Visible || _readingPane.ActualWidth <= 2 || _readingPane.ActualHeight <= 0)
            return;

        int paneX = _readingPane.ActualX;
        int paneY = _readingPane.ActualY;
        int paneW = _readingPane.ActualWidth;
        int paneH = _readingPane.ActualHeight;
        int scrollOffset = _readingPane.VerticalScrollOffset;

        var paneBounds = new LayoutRect(paneX, paneY, paneW, paneH);

        IReadOnlyList<IWindowControl> children;
        try { children = _readingPane.GetChildren(); }
        catch { return; }

        // Compute positions from content order + scroll offset
        // This avoids relying on stale ActualY from off-screen controls
        int contentY = -scrollOffset;

        foreach (var child in children)
        {
            if (!child.Visible)
                continue;

            int childHeight = child.ActualHeight > 0 ? child.ActualHeight : 1;
            int screenY = paneY + contentY;

            // Skip if entirely above or below viewport
            if (screenY + childHeight <= paneY || screenY >= paneY + paneH)
            {
                contentY += childHeight;
                continue;
            }

            var tag = (child as BaseControl)?.Tag as string;
            if (tag != null)
            {
                var rect = new LayoutRect(paneX, screenY, paneW, childHeight);
                var clipped = Intersect(rect, paneBounds);

                if (clipped.Width > 0 && clipped.Height > 0)
                {
                    switch (tag)
                    {
                        case "header":
                            ColorBlendHelper.ApplyColorOverlay(buffer, HeaderTintColor, HeaderTintIntensity, HeaderFgRatio, clipped);
                            break;
                        case "attachments":
                            ColorBlendHelper.ApplyColorOverlay(buffer, AttachmentTintColor, AttachmentTintIntensity, AttachmentFgRatio, clipped);
                            break;
                        case "quote":
                            ColorBlendHelper.ApplyColorOverlay(buffer, QuoteDimColor, QuoteDimIntensity, QuoteFgRatio, clipped);
                            break;
                        case "signature":
                            var bgColor = buffer.GetCell(clipped.X, clipped.Y).Background;
                            ColorBlendHelper.ApplyColorOverlay(buffer, bgColor, SignatureDimIntensity, SignatureFgRatio, clipped);
                            break;
                    }
                }
            }

            contentY += childHeight;
        }
    }

    private static LayoutRect Intersect(LayoutRect a, LayoutRect b)
    {
        int x = Math.Max(a.X, b.X);
        int y = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= x || bottom <= y) return new LayoutRect(0, 0, 0, 0);
        return new LayoutRect(x, y, right - x, bottom - y);
    }
}
