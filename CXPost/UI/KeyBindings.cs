namespace CXPost.UI;

public static class KeyBindings
{
    // Compose
    public static readonly ConsoleKey ComposeNew = ConsoleKey.N;           // Ctrl+N
    public static readonly ConsoleKey Reply = ConsoleKey.R;                // Ctrl+R
    public static readonly ConsoleKey Forward = ConsoleKey.F;              // Ctrl+F
    public static readonly ConsoleKey Send = ConsoleKey.Enter;             // Ctrl+Enter

    // Navigation
    public static readonly ConsoleKey Search = ConsoleKey.S;               // Ctrl+S
    public static readonly ConsoleKey MoveToFolder = ConsoleKey.M;         // Ctrl+M
    public static readonly ConsoleKey Refresh = ConsoleKey.F5;             // F5
    public static readonly ConsoleKey SwitchLayout = ConsoleKey.F8;        // F8
    public static readonly ConsoleKey Settings = ConsoleKey.OemComma;      // Ctrl+,

    // Message actions
    public static readonly ConsoleKey Delete = ConsoleKey.Delete;          // Del
    public static readonly ConsoleKey ToggleFlag = ConsoleKey.D;           // Ctrl+D
    public static readonly ConsoleKey ToggleRead = ConsoleKey.U;           // Ctrl+U

    // Reserved: SaveDraft (Ctrl+S conflicts with Search — not yet implemented)
}
