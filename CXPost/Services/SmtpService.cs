using CXPost.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CXPost.Services;

public class SmtpService : ISmtpService, IDisposable
{
    private readonly ICredentialService _credentials;
    private SmtpClient? _client;

    public SmtpService(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public bool IsConnected => _client?.IsConnected == true && _client.IsAuthenticated;

    public async Task ConnectAsync(Account account, CancellationToken ct = default)
    {
        _client = new SmtpClient();
        var socketOptions = account.SmtpSecurity switch
        {
            SecurityType.Ssl => SecureSocketOptions.SslOnConnect,
            SecurityType.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };
        await _client.ConnectAsync(account.SmtpHost, account.SmtpPort, socketOptions, ct);

        var password = _credentials.GetPassword(account.Id) ?? string.Empty;
        await _client.AuthenticateAsync(account.Username.Length > 0 ? account.Username : account.Email, password, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client is { IsConnected: true })
            await _client.DisconnectAsync(true, ct);
    }

    public async Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        if (_client is not { IsConnected: true })
            throw new InvalidOperationException("Not connected to SMTP server. Call ConnectAsync first.");

        await _client.SendAsync(message, ct);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
