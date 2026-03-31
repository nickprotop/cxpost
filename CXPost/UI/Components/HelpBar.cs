namespace CXPost.UI.Components;

/// <summary>
/// A bottom help bar with clickable shortcut items.
/// Format: "[cyan1]shortcut[/][grey70]:label  [/]" per item.
/// Tracks item positions for mouse click handling.
/// </summary>
public class HelpBar : ClickableBar
{
    private readonly int _marginLeft;

    public HelpBar(int marginLeft = 1)
    {
        _marginLeft = marginLeft;
    }

    /// <summary>
    /// Add a shortcut item to the bar.
    /// </summary>
    public HelpBar Add(string shortcut, string label, Action? onClick = null)
    {
        _items.Add(new BarItem
        {
            Shortcut = shortcut,
            Label = label,
            OnClick = onClick
        });
        return this;
    }

    /// <summary>
    /// Render the bar as a markup string and record StartX/EndX for each item.
    /// </summary>
    public override string Render()
    {
        var parts = new List<string>();
        int pos = 0;
        const string separator = "  ";

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            // Plain text length: "shortcut:label"
            int plainLen = item.Shortcut.Length + 1 + item.Label.Length;
            item.StartX = pos;
            item.EndX = pos + plainLen;

            parts.Add($"[cyan1]{item.Shortcut}[/][grey70]:{item.Label}[/]");

            pos += plainLen;

            if (i < _items.Count - 1)
            {
                pos += separator.Length;
            }
        }

        TotalRenderedLength = pos;
        return string.Join(separator, parts);
    }

    /// <summary>
    /// Handle a mouse click at the given x position (control-relative).
    /// Adjusts for the left margin before hit-testing.
    /// </summary>
    public bool HandleClick(int x)
    {
        return HandleClickAt(x - _marginLeft);
    }
}
