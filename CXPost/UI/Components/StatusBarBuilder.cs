using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Parsing;

namespace CXPost.UI.Components;

/// <summary>
/// Manages status bar markup controls for top/bottom panel display.
/// Provides methods to update breadcrumb, connection status, and help text.
/// </summary>
public class StatusBarBuilder
{
    private readonly MarkupControl _topLeft;
    private readonly MarkupControl _topRight;
    private readonly MarkupControl _bottomLeft;

    public StatusBarBuilder()
    {
        _topLeft = Controls.Markup("[cyan1]CXPost[/]").Build();
        _topRight = Controls.Markup("[grey50]Disconnected[/]").Build();
        _bottomLeft = Controls.Markup("[grey50]Ctrl+N[/]: Compose  [grey50]Ctrl+R[/]: Reply  [grey50]Ctrl+S[/]: Search  [grey50]Del[/]: Delete  [grey50]Ctrl+M[/]: Move").Build();
    }

    public MarkupControl TopLeftControl => _topLeft;
    public MarkupControl TopRightControl => _topRight;
    public MarkupControl BottomLeftControl => _bottomLeft;

    public void UpdateBreadcrumb(string accountName, string folderName)
    {
        _topLeft.SetContent([$"[cyan1]CXPost[/] [grey50]|[/] [{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(accountName)}[/] [grey50]>[/] {MarkupParser.Escape(folderName)}"]);
    }

    public void UpdateConnectionStatus(int unreadCount, bool connected)
    {
        var status = connected
            ? $"[{ColorScheme.FlaggedMarkup}]{unreadCount} unread[/] [grey50]|[/] [{ColorScheme.SuccessMarkup}]● Connected[/]"
            : $"[{ColorScheme.ErrorMarkup}]● Offline[/]";
        _topRight.SetContent([status]);
    }

    public void UpdateHelpBar(string context)
    {
        _bottomLeft.SetContent([context]);
    }
}
