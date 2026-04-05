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
/// </summary>
public class AutocompletePortalContent : PortalContentBase
{
    private readonly BaseControl _anchor;
    private List<string> _items = [];
    private int _selectedIndex;
    private int _scrollOffset;
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
    }

    public bool HasItems => _items.Count > 0;
    public int ItemCount => _items.Count;

    public void UpdateItems(List<string> items)
    {
        _items = items;
        _selectedIndex = 0;
        _scrollOffset = 0;
        Container?.Invalidate(true);
    }

    public void MoveUp()
    {
        if (_items.Count == 0 || _selectedIndex <= 0) return;
        _selectedIndex--;
        EnsureVisible();
        Container?.Invalidate(true);
    }

    public void MoveDown()
    {
        if (_items.Count == 0) return;
        _selectedIndex = (_selectedIndex + 1) % _items.Count;
        EnsureVisible();
        Container?.Invalidate(true);
    }

    public string? GetSelectedItem()
    {
        if (_items.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _items.Count)
            return null;
        return _items[_selectedIndex];
    }

    private void EnsureVisible()
    {
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        else if (_selectedIndex >= _scrollOffset + MaxVisibleItems)
            _scrollOffset = _selectedIndex - MaxVisibleItems + 1;
    }

    public override Rectangle GetPortalBounds()
    {
        var visibleCount = Math.Min(_items.Count, MaxVisibleItems);
        // +2 for border
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

        // Account for border offset (already adjusted by base class when BorderStyle is set)
        var row = args.Position.Y;
        var itemIndex = row + _scrollOffset;
        if (itemIndex >= 0 && itemIndex < _items.Count)
        {
            _selectedIndex = itemIndex;
            ItemSelected?.Invoke(_items[itemIndex]);
            return true;
        }
        return false;
    }

    protected override void PaintPortalContent(CharacterBuffer buffer, LayoutRect bounds,
        LayoutRect clipRect, Color defaultFg, Color defaultBg)
    {
        var visibleCount = Math.Min(_items.Count, MaxVisibleItems);
        var bg = ColorScheme.WindowBackground;
        var selectedBg = ColorScheme.PanelHeaderBackground;

        for (var i = 0; i < visibleCount; i++)
        {
            var itemIndex = i + _scrollOffset;
            if (itemIndex >= _items.Count) break;

            var isSelected = itemIndex == _selectedIndex;
            var rowBg = isSelected ? selectedBg : bg;
            var textColor = isSelected ? Color.White : Color.Grey70;

            var y = bounds.Y + i;
            if (y < clipRect.Y || y >= clipRect.Bottom) continue;

            // Clear row
            for (var x = 0; x < bounds.Width; x++)
                buffer.SetNarrowCell(bounds.X + x, y, ' ', textColor, rowBg);

            // Render text (strip markup — items are plain text from ContactsService)
            var text = _items[itemIndex];
            if (text.Length > bounds.Width - 2) text = text[..(bounds.Width - 2)];
            for (var c = 0; c < text.Length; c++)
                buffer.SetNarrowCell(bounds.X + 1 + c, y, text[c], textColor, rowBg);
        }
    }
}
