using SharpConsoleUI;

namespace CXPost.UI.Components;

public static class ColorScheme
{
    // Backgrounds
    public static readonly Color WindowBackground = Color.Grey11;
    public static readonly Color PanelBackground = Color.Grey15;
    public static readonly Color StatusBarBackground = Color.Grey15;
    public static readonly Color HeaderBackground = Color.Grey19;
    public static readonly Color SidebarBackground = Color.Grey19;

    // Borders
    public static readonly Color BorderColor = Color.Grey23;
    public static readonly Color ActiveBorderColor = Color.SteelBlue;

    // Semi-transparent panel header — lets the gradient bleed through
    public static readonly Color PanelHeaderBackground = new(40, 50, 70, 160);

    // Text
    public static readonly Color PrimaryText = Color.Cyan1;
    public static readonly Color SecondaryText = Color.Grey70;
    public static readonly Color MutedText = Color.Grey50;

    // Status
    public static readonly Color Success = Color.Green;
    public static readonly Color Warning = Color.Yellow;
    public static readonly Color Error = Color.Red;
    public static readonly Color Info = Color.Cyan1;

    // Mail-specific
    public static readonly Color Unread = Color.White;
    public static readonly Color Read = Color.Grey50;
    public static readonly Color Flagged = Color.Yellow;
    public static readonly Color SelectedRow = Color.SteelBlue;

    // Markup strings
    public const string PrimaryMarkup = "cyan1";
    public const string MutedMarkup = "grey50";
    public const string UnreadMarkup = "white bold";
    public const string ReadMarkup = "grey50";
    public const string FlaggedMarkup = "yellow";
    public const string ErrorMarkup = "red";
    public const string SuccessMarkup = "green";
}
