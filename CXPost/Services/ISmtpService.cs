using CXPost.Models;

namespace CXPost.Services;

public interface ISmtpService
{
    bool IsConnected { get; }
    Task ConnectAsync(Account account, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendAsync(MimeKit.MimeMessage message, CancellationToken ct = default);
}
