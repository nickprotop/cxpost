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

    public void NotifyNewMail(string from, string subject)
    {
        _ws.NotificationStateService.ShowNotification(
            "New Mail",
            $"From: {from}\n{subject}",
            NotificationSeverity.Info,
            timeout: 5000);
    }

    public void NotifySendSuccess(string to)
    {
        _ws.NotificationStateService.ShowNotification(
            "Sent",
            $"Message sent to {to}",
            NotificationSeverity.Success,
            timeout: 3000);
    }

    public void NotifyError(string title, string message)
    {
        _ws.NotificationStateService.ShowNotification(
            title, message, NotificationSeverity.Danger, timeout: 5000);
    }
}
