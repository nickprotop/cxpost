using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;

namespace CXPost.UI.Components;

public class ReadingPaneCompositor
{
    private readonly ScrollablePanelControl _readingPane;
    private bool _regionsDirty = true;

    // Cached regions
    private LayoutRect _headerRect;
    private int _gradientSeparatorY = -1;
    private readonly List<LayoutRect> _quoteRects = new();
    private readonly List<LayoutRect> _signatureRects = new();

    // Overlay parameters
    private static readonly Color HeaderTintColor = new(100, 130, 170);
    private const float HeaderTintIntensity = 0.07f;
    private const float HeaderFgRatio = 0.3f;

    private static readonly Color QuoteDimColor = new(80, 80, 100);
    private const float QuoteDimIntensity = 0.12f;
    private const float QuoteFgRatio = 0.3f;

    private const float SignatureDimIntensity = 0.55f;
    private const float SignatureFgRatio = 0.5f;

    private static readonly Color GradientStartColor = new(85, 85, 85);

    public ReadingPaneCompositor(ScrollablePanelControl readingPane)
    {
        _readingPane = readingPane;
        _readingPane.Scrolled += (_, _) => Invalidate();
    }

    public void Invalidate()
    {
        _regionsDirty = true;
    }

    public void OnPaint(CharacterBuffer buffer, LayoutRect dirtyRegion, LayoutRect clipRect)
    {
        if (!_readingPane.Visible || _readingPane.ActualWidth <= 0 || _readingPane.ActualHeight <= 0)
            return;

        if (_regionsDirty)
        {
            RecalculateRegions();
            _regionsDirty = false;
        }

        var paneBounds = new LayoutRect(
            _readingPane.ActualX, _readingPane.ActualY,
            _readingPane.ActualWidth, _readingPane.ActualHeight);

        // 1. Header block tint
        if (_headerRect.Width > 0 && _headerRect.Height > 0)
        {
            var clipped = Intersect(_headerRect, paneBounds);
            if (clipped.Width > 0 && clipped.Height > 0)
                ColorBlendHelper.ApplyColorOverlay(buffer, HeaderTintColor, HeaderTintIntensity, HeaderFgRatio, clipped);
        }

        // 2. Gradient separator below header
        if (_gradientSeparatorY >= paneBounds.Y && _gradientSeparatorY < paneBounds.Bottom)
        {
            int gradientWidth = (int)(paneBounds.Width * 0.6);
            if (gradientWidth > 0)
            {
                var bgColor = buffer.GetCell(paneBounds.X, _gradientSeparatorY).Background;
                var gradientRect = new LayoutRect(paneBounds.X, _gradientSeparatorY, gradientWidth, 1);
                buffer.GradientFillHorizontal(gradientRect, '\u2500', GradientStartColor, bgColor, bgColor, paneBounds);
            }
        }

        // 3. Quote block dim
        foreach (var quoteRect in _quoteRects)
        {
            var clipped = Intersect(quoteRect, paneBounds);
            if (clipped.Width > 0 && clipped.Height > 0)
                ColorBlendHelper.ApplyColorOverlay(buffer, QuoteDimColor, QuoteDimIntensity, QuoteFgRatio, clipped);
        }

        // 4. Signature dim
        foreach (var sigRect in _signatureRects)
        {
            var clipped = Intersect(sigRect, paneBounds);
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                var bgColor = buffer.GetCell(sigRect.X, Math.Max(sigRect.Y, paneBounds.Y)).Background;
                ColorBlendHelper.ApplyColorOverlay(buffer, bgColor, SignatureDimIntensity, SignatureFgRatio, clipped);
            }
        }
    }

    private void RecalculateRegions()
    {
        _headerRect = default;
        _gradientSeparatorY = -1;
        _quoteRects.Clear();
        _signatureRects.Clear();

        // Use GetChildren() to enumerate child controls
        var children = _readingPane.GetChildren();

        foreach (var child in children)
        {
            var tag = (child as BaseControl)?.Tag as string;

            var rect = new LayoutRect(child.ActualX, child.ActualY, child.ActualWidth, child.ActualHeight);
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            switch (tag)
            {
                case "header":
                    _headerRect = rect;
                    _gradientSeparatorY = rect.Bottom;
                    break;
                case "quote":
                    _quoteRects.Add(rect);
                    break;
                case "signature":
                    _signatureRects.Add(rect);
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
