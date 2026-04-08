using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;

namespace CXPost.UI.Components;

public class ReadingPaneCompositor
{
    private readonly ScrollablePanelControl _readingPane;

    // Overlay parameters
    private static readonly Color HeaderTintColor = new(100, 130, 170);
    private const float HeaderTintIntensity = 0.07f;
    private const float HeaderFgRatio = 0.3f;

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
        if (!_readingPane.Visible || _readingPane.ActualWidth <= 0 || _readingPane.ActualHeight <= 0)
            return;

        var paneBounds = new LayoutRect(
            _readingPane.ActualX, _readingPane.ActualY,
            _readingPane.ActualWidth, _readingPane.ActualHeight);

        // Scan children for tagged controls and apply overlays directly
        foreach (var child in _readingPane.GetChildren())
        {
            var tag = (child as BaseControl)?.Tag as string;
            if (tag == null) continue;

            var rect = new LayoutRect(child.ActualX, child.ActualY, child.ActualWidth, child.ActualHeight);
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            var clipped = Intersect(rect, paneBounds);
            if (clipped.Width <= 0 || clipped.Height <= 0) continue;

            switch (tag)
            {
                case "header":
                    ColorBlendHelper.ApplyColorOverlay(buffer, HeaderTintColor, HeaderTintIntensity, HeaderFgRatio, clipped);
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
