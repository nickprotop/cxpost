using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Parsing;

namespace CXPost.UI.Components;

/// <summary>
/// Manages status bar controls for top/bottom panel display.
/// Top-left is a clickable breadcrumb via StatusBarControl.
/// </summary>
public class StatusBarBuilder
{
    private readonly StatusBarControl _topLeft;
    private readonly MarkupControl _topRight;
    private readonly MarkupControl _bottomLeft;

    public StatusBarBuilder()
    {
        _topLeft = Controls.StatusBar()
            .AddLeftText("[cyan1]CXPost[/]")
            .Build();
        _topLeft.SeparatorChar = "\u203a";
        _topLeft.BackgroundColor = Color.Transparent;

        _topRight = Controls.Markup("[grey50]Disconnected[/]").Build();
        _bottomLeft = Controls.Markup("[grey50]Ctrl+N[/]: Compose  [grey50]Ctrl+R[/]: Reply  [grey50]Ctrl+S[/]: Search  [grey50]Del[/]: Delete  [grey50]Ctrl+M[/]: Move").Build();
    }

    public StatusBarControl TopLeftControl => _topLeft;
    public MarkupControl TopRightControl => _topRight;
    public MarkupControl BottomLeftControl => _bottomLeft;

    public void UpdateBreadcrumb(string accountName, string folderName,
        Action? onAppClick = null, Action? onAccountClick = null)
    {
        _topLeft.ClearAll();
        _topLeft.AddLeftText($"[cyan1]CXPost[/]", onAppClick);
        _topLeft.AddLeftSeparator();
        if (onAccountClick != null)
            _topLeft.AddLeftText($"[{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(accountName)}[/]", onAccountClick);
        else
            _topLeft.AddLeftText($"[{ColorScheme.PrimaryMarkup}]{MarkupParser.Escape(accountName)}[/]");
        _topLeft.AddLeftSeparator();
        _topLeft.AddLeftText(MarkupParser.Escape(folderName));
    }

    public void UpdateConnectionStatus(int unreadCount, bool connected)
    {
        var time = DateTime.Now.ToString("h:mm tt");
        var status = connected
            ? $"[{ColorScheme.FlaggedMarkup}]{unreadCount} unread[/] [grey50]|[/] [{ColorScheme.SuccessMarkup}]\u25cf Connected[/] [grey50]|[/] [grey70]{time}[/]"
            : $"[{ColorScheme.ErrorMarkup}]\u25cf Offline[/] [grey50]|[/] [grey70]{time}[/]";
        _topRight.SetContent([status]);
    }

    public void UpdateHelpBar(string context)
    {
        _bottomLeft.SetContent([context]);
    }
}
