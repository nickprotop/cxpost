using System.Collections.Concurrent;
using CXPost.Models;

namespace CXPost.Services;

public class ImapConnectionFactory : IDisposable
{
    private readonly ICredentialService _credentials;
    private readonly ConcurrentDictionary<string, ImapService> _syncConnections = new();
    private readonly ConcurrentDictionary<string, ImapService> _fetchConnections = new();
    private readonly ConcurrentDictionary<string, ImapService> _idleConnections = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _syncLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new();

    public ImapConnectionFactory(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public ICredentialService Credentials => _credentials;

    /// <summary>Persistent sync connection (folder list, header fetch, periodic sync).</summary>
    public ImapService GetSyncConnection(Account account)
        => _syncConnections.GetOrAdd(account.Id, _ => new ImapService(_credentials));

    /// <summary>Persistent fetch connection (body, flags, attachments, search, move, delete).</summary>
    public ImapService GetFetchConnection(Account account)
        => _fetchConnections.GetOrAdd(account.Id, _ => new ImapService(_credentials));

    /// <summary>Persistent IDLE connection (push notifications).</summary>
    public ImapService GetIdleConnection(Account account)
        => _idleConnections.GetOrAdd(account.Id, _ => new ImapService(_credentials));

    public SemaphoreSlim GetSyncLock(string accountId)
        => _syncLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

    public SemaphoreSlim GetFetchLock(string accountId)
        => _fetchLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

    // Legacy compatibility — maps to sync connection
    public ImapService GetConnection(Account account) => GetSyncConnection(account);
    public SemaphoreSlim GetLock(string accountId) => GetSyncLock(accountId);

    public bool HasAnyConnection =>
        _syncConnections.Values.Any(c => c.IsConnected) ||
        _fetchConnections.Values.Any(c => c.IsConnected) ||
        _idleConnections.Values.Any(c => c.IsConnected);

    /// <summary>Ensures a connection is connected, reconnecting if needed.</summary>
    public async Task EnsureConnectedAsync(ImapService imap, Account account, CancellationToken ct)
    {
        if (!imap.IsConnected)
            await imap.ConnectAsync(account, ct);
    }

    /// <summary>
    /// Gets the fetch connection, acquires the fetch lock, ensures connected, runs the operation,
    /// and retries once with a fresh reconnect if the operation fails (and ct is not cancelled).
    /// Releases the lock in finally.
    /// </summary>
    public async Task<T> WithFetchConnectionAsync<T>(Account account,
        Func<ImapService, Task<T>> operation, CancellationToken ct)
    {
        var imap = GetFetchConnection(account);
        var fetchLock = GetFetchLock(account.Id);
        await fetchLock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(imap, account, ct);
            try
            {
                return await operation(imap);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                try { await imap.DisconnectAsync(CancellationToken.None); } catch { }
                await imap.ConnectAsync(account, ct);
                return await operation(imap);
            }
        }
        finally { fetchLock.Release(); }
    }

    /// <summary>
    /// Gets the fetch connection, acquires the fetch lock, ensures connected, runs the operation,
    /// and retries once with a fresh reconnect if the operation fails (and ct is not cancelled).
    /// Releases the lock in finally.
    /// </summary>
    public async Task WithFetchConnectionAsync(Account account,
        Func<ImapService, Task> operation, CancellationToken ct)
    {
        await WithFetchConnectionAsync<object?>(account, async imap =>
        {
            await operation(imap);
            return null;
        }, ct);
    }

    public async Task ResetConnectionAsync(string accountId, CancellationToken ct = default)
    {
        await ResetOne(_syncConnections, accountId, ct);
        await ResetOne(_fetchConnections, accountId, ct);
        await ResetOne(_idleConnections, accountId, ct);
    }

    private static async Task ResetOne(ConcurrentDictionary<string, ImapService> dict, string accountId, CancellationToken ct)
    {
        if (dict.TryRemove(accountId, out var service))
        {
            try { await service.DisconnectAsync(ct); }
            catch { }
            service.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var c in _syncConnections.Values) c.Dispose();
        foreach (var c in _fetchConnections.Values) c.Dispose();
        foreach (var c in _idleConnections.Values) c.Dispose();
        foreach (var s in _syncLocks.Values) s.Dispose();
        foreach (var s in _fetchLocks.Values) s.Dispose();
    }
}
