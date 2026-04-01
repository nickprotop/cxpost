using System.Collections.Concurrent;
using CXPost.Models;

namespace CXPost.Services;

/// <summary>
/// Creates and manages per-account IImapService instances with concurrency guards.
/// </summary>
public class ImapConnectionFactory : IDisposable
{
    private readonly ICredentialService _credentials;
    private readonly ConcurrentDictionary<string, ImapService> _connections = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ImapConnectionFactory(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public ICredentialService Credentials => _credentials;

    /// <summary>
    /// Gets or creates an ImapService for the given account.
    /// </summary>
    public ImapService GetConnection(Account account)
    {
        return _connections.GetOrAdd(account.Id, _ => new ImapService(_credentials));
    }

    /// <summary>
    /// Gets the concurrency lock for the given account.
    /// </summary>
    public SemaphoreSlim GetLock(string accountId)
    {
        return _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Returns true if any account has an active connection.
    /// </summary>
    public bool HasAnyConnection => _connections.Values.Any(c => c.IsConnected);

    /// <summary>
    /// Disconnects and removes the connection for the given account.
    /// </summary>
    public async Task ResetConnectionAsync(string accountId, CancellationToken ct = default)
    {
        if (_connections.TryRemove(accountId, out var service))
        {
            try { await service.DisconnectAsync(ct); }
            catch { /* best effort */ }
            service.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var conn in _connections.Values)
            conn.Dispose();
        foreach (var sem in _locks.Values)
            sem.Dispose();
    }
}
