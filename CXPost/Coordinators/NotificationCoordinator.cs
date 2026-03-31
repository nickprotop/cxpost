using CXPost.UI;
using CXPost.UI.Components;

namespace CXPost.Coordinators;

public class NotificationCoordinator
{
    private readonly Lazy<CXPostApp> _app;

    public NotificationCoordinator(Lazy<CXPostApp> app)
    {
        _app = app;
    }

    public void NotifyNewMail(string from, string subject) =>
        _app.Value.EnqueueUiAction(() =>
            _app.Value.ShowInfo($"📬 New mail from {from}: {subject}"));

    public void NotifySendSuccess(string to) =>
        _app.Value.EnqueueUiAction(() =>
            _app.Value.ShowSuccess($"✉ Message sent to {to}"));

    public void NotifySyncComplete(string accountName, int newMessages)
    {
        var msg = newMessages > 0
            ? $"⟳ {accountName}: {newMessages} new message{(newMessages != 1 ? "s" : "")}"
            : $"⟳ {accountName}: Up to date";
        _app.Value.EnqueueUiAction(() =>
            _app.Value.ShowSuccess(msg));
    }

    public void NotifyError(string title, string message) =>
        _app.Value.EnqueueUiAction(() =>
            _app.Value.ShowError($"{title}: {message}"));

    public void Dismiss(string id) =>
        _app.Value.EnqueueUiAction(() =>
            _app.Value.DismissMessage(id));
}
