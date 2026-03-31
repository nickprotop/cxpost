namespace CXPost.UI.Components;

/// <summary>
/// Represents a single clickable item in a bar (shortcut + label).
/// StartX/EndX are populated during Render() for hit-testing.
/// </summary>
public class BarItem
{
    public string Shortcut { get; set; } = "";
    public string Label { get; set; } = "";
    public Action? OnClick { get; set; }
    public int StartX { get; set; }
    public int EndX { get; set; }
}

/// <summary>
/// Base class for bars with clickable items. Each item has a shortcut label,
/// a display label, and an optional click action. During Render(), the
/// positions of each item are recorded for mouse hit-testing.
/// </summary>
public abstract class ClickableBar
{
    protected readonly List<BarItem> _items = new();

    /// <summary>
    /// Total rendered length (in columns) of the last Render() call.
    /// </summary>
    public int TotalRenderedLength { get; protected set; }

    /// <summary>
    /// Render the bar to a markup string and record item positions.
    /// </summary>
    public abstract string Render();

    /// <summary>
    /// Clear all items.
    /// </summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// Attempt to handle a click at the given column offset.
    /// Returns true if a matching item was found and its action invoked.
    /// </summary>
    protected bool HandleClickAt(int col)
    {
        foreach (var item in _items)
        {
            if (col >= item.StartX && col < item.EndX && item.OnClick != null)
            {
                item.OnClick.Invoke();
                return true;
            }
        }
        return false;
    }
}
