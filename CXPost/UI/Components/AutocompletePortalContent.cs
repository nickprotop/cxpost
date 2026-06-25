using System.Drawing;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using Color = SharpConsoleUI.Color;

namespace CXPost.UI.Components;

/// <summary>
/// Portal content that renders a list of autocomplete suggestions below a prompt control.
/// Hosts a real <see cref="ListControl"/> as its <see cref="PortalContentBase.Content"/>, so the
/// base class measures/paints the list through the DOM and the list self-invalidates the window
/// (child → portal → window) — no manual <c>Invalidate</c> calls are needed.
/// </summary>
public class AutocompletePortalContent : PortalContentBase
{
    private readonly BaseControl _anchor;
    private readonly ListControl _list;
    private const int MaxVisibleItems = 8;

    /// <summary>Fired when the user confirms a selection (Enter/Tab/Click).</summary>
    public event Action<string>? ItemSelected;

    public AutocompletePortalContent(BaseControl anchor)
    {
        _anchor = anchor;
        DismissOnOutsideClick = true;
        BorderStyle = BoxChars.Rounded;
        BorderColor = Color.Grey35;
        BorderBackgroundColor = ColorScheme.WindowBackground;

        _list = new ListControl
        {
            BackgroundColor = ColorScheme.WindowBackground,
            ForegroundColor = Color.Grey70,
            FocusedBackgroundColor = ColorScheme.WindowBackground,
            FocusedForegroundColor = Color.Grey70,
            HighlightBackgroundColor = ColorScheme.PanelHeaderBackground,
            HighlightForegroundColor = Color.White,
            HoverHighlightsItems = false,
            AutoAdjustWidth = false,
        };

        // Host the list as the portal's Content: the base measures/paints it through the DOM and
        // wires its Container → self-invalidation (child → portal → window).
        Content = _list;

        // Mark the list as portal-focused so the selected row uses HighlightBackground.
        PortalFocusedControl = _list;
    }

    public bool HasItems => _list.Items.Count > 0;
    public int ItemCount => _list.Items.Count;

    public void UpdateItems(List<string> items)
    {
        // StringItems setter self-invalidates (Relayout); no manual Invalidate needed.
        _list.StringItems = items;
        if (items.Count > 0)
            _list.SelectedIndex = 0;
    }

    public void MoveUp()
    {
        // SelectedIndex setter self-invalidates and ensures-visible/scrolls automatically.
        if (_list.SelectedIndex > 0)
            _list.SelectedIndex--;
    }

    public void MoveDown()
    {
        // Wrap-around; SelectedIndex setter self-invalidates and ensures-visible automatically.
        if (_list.Items.Count > 0)
            _list.SelectedIndex = (_list.SelectedIndex + 1) % _list.Items.Count;
    }

    public string? GetSelectedItem() => _list.SelectedItem?.Text;

    public override Rectangle GetPortalBounds()
    {
        var visibleCount = Math.Min(_list.Items.Count, MaxVisibleItems);
        // +2 for border (the base shrinks to the inner rect for the hosted list)
        var height = visibleCount + 2;
        var width = _anchor.ActualWidth;
        if (width < 20) width = 40;

        var request = new PortalPositionRequest(
            Anchor: new Rectangle(_anchor.ActualX, _anchor.ActualY, _anchor.ActualWidth, _anchor.ActualHeight),
            ContentSize: new Size(width, height),
            ScreenBounds: new Rectangle(0, 0,
                _anchor.GetParentWindow()?.Width ?? 80,
                _anchor.GetParentWindow()?.Height ?? 24),
            Placement: PortalPlacement.BelowOrAbove
        );

        var result = PortalPositioner.Calculate(request);
        return result.Bounds;
    }

    public override bool ProcessMouseEvent(MouseEventArgs args)
    {
        if (!args.HasAnyFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Clicked))
            return false;

        // Forward to the hosted list: it updates its own selection from the click row,
        // correctly accounting for its internal scroll offset.
        ProcessHostedMouseEvent(args);

        var selected = GetSelectedItem();
        if (selected != null)
            ItemSelected?.Invoke(selected);
        return true;
    }

    // Content is hosted via the base class, so this is never called (the base paints the hosted
    // Content child directly). Kept only because PaintPortalContent is abstract.
    protected override void PaintPortalContent(CharacterBuffer buffer, LayoutRect bounds,
        LayoutRect clipRect, Color defaultFg, Color defaultBg)
    { }
}
