using SharpConsoleUI;
using SharpConsoleUI.Core;

namespace CXPost.Coordinators;

public class NotificationCoordinator
{
    private readonly ConsoleWindowSystem _ws;

    public NotificationCoordinator(ConsoleWindowSystem ws)
    {
        _ws = ws;
    }

    public string NotifyNewMail(string from, string subject) =>
        _ws.NotificationStateService.ShowNotification(
            "📬 New Mail",
            $"From: {from}\n{subject}",
            NotificationSeverity.Info,
            timeout: 5000);

    public string NotifySendSuccess(string to) =>
        _ws.NotificationStateService.ShowNotification(
            "✉ Sent",
            $"Message sent to {to}",
            NotificationSeverity.Success,
            timeout: 3000);

    public string NotifySyncComplete(string accountName, int newMessages)
    {
        var msg = newMessages > 0
            ? $"{newMessages} new message{(newMessages != 1 ? "s" : "")}"
            : "Up to date";
        return _ws.NotificationStateService.ShowNotification(
            $"⟳ {accountName}",
            msg,
            NotificationSeverity.Success,
            timeout: 4000);
    }

    public string NotifyError(string title, string message) =>
        _ws.NotificationStateService.ShowNotification(
            $"✗ {title}", message, NotificationSeverity.Danger, timeout: 8000);

    public void Dismiss(string id) =>
        _ws.NotificationStateService.DismissNotification(id);
}
