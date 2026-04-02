using System.Collections.Concurrent;
using CXPost.Models;

namespace CXPost.Services;

/// <summary>
/// Manages IMAP connections with two strategies:
/// - Persistent: one sync + one idle connection per account (long-lived, lock-protected)
/// - Ephemeral: created per user operation, caller owns and disposes (no locks needed)
/// </summary>
public class ImapConnectionFactory : IDisposable
{
    private readonly ICredentialService _credentials;
    private readonly ConcurrentDictionary<string, ImapService> _syncConnections = new();
    private readonly ConcurrentDictionary<string, ImapService> _idleConnections = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _syncLocks = new();

    public ImapConnectionFactory(ICredentialService credentials)
    {
        _credentials = credentials;
    }

    public ICredentialService Credentials => _credentials;

    // ── Persistent connections (sync + idle only) ───────────────────────

    public ImapService GetSyncConnection(Account account)
    {
        var imap = _syncConnections.GetOrAdd(account.Id, _ =>
        {
            ImapLogger.Debug($"[{account.Name}] Creating persistent sync connection");
            return new ImapService(_credentials);
        });
        return imap;
    }

    public ImapService GetIdleConnection(Account account)
    {
        var imap = _idleConnections.GetOrAdd(account.Id, _ =>
        {
            ImapLogger.Debug($"[{account.Name}] Creating persistent IDLE connection");
            return new ImapService(_credentials);
        });
        return imap;
    }

    public SemaphoreSlim GetSyncLock(string accountId)
        => _syncLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

    public async Task EnsureConnectedAsync(ImapService imap, Account account, CancellationToken ct)
    {
        if (!imap.IsConnected)
        {
            ImapLogger.Debug($"[{account.Name}] Reconnecting persistent connection to {account.ImapHost}:{account.ImapPort}");
            await imap.ConnectAsync(account, ct);
            ImapLogger.Debug($"[{account.Name}] Persistent connection established");
        }
    }

    // ── Ephemeral connections (user operations) ─────────────────────────

    public async Task<ImapService> CreateConnectionAsync(Account account, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString()[..8];
        ImapLogger.Debug($"[{account.Name}] Ephemeral({id}) connecting to {account.ImapHost}:{account.ImapPort}");
        var imap = new ImapService(_credentials);
        try
        {
            await imap.ConnectAsync(account, ct);
            ImapLogger.Debug($"[{account.Name}] Ephemeral({id}) connected");
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            // MailKit wraps TaskCanceledException inside SslHandshakeException
            // when cancellation happens during SSL handshake
            ImapLogger.Debug($"[{account.Name}] Ephemeral({id}) cancelled during connect");
            imap.Dispose();
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            ImapLogger.Warn($"[{account.Name}] Ephemeral({id}) connect failed: {ex.Message} — retrying in 1s");
            imap.Dispose();
            await Task.Delay(1000, ct);
            imap = new ImapService(_credentials);
            try
            {
                await imap.ConnectAsync(account, ct);
                ImapLogger.Debug($"[{account.Name}] Ephemeral({id}) retry connected");
            }
            catch (Exception retryEx)
            {
                ImapLogger.Error($"[{account.Name}] Ephemeral({id}) retry also failed: {retryEx.Message}", retryEx);
                imap.Dispose();
                throw;
            }
        }
        return imap;
    }

    // ── Status ──────────────────────────────────────────────────────────

    public bool HasAnyConnection =>
        _syncConnections.Values.Any(c => c.IsConnected) ||
        _idleConnections.Values.Any(c => c.IsConnected);

    // ── Reset (after settings change) ───────────────────────────────────

    public async Task ResetAllAsync(string accountId, CancellationToken ct = default)
    {
        ImapLogger.Info($"Resetting all connections for account {accountId}");
        if (_syncConnections.TryRemove(accountId, out var sync))
        {
            try { await sync.DisconnectAsync(ct); } catch { }
            sync.Dispose();
        }
        if (_idleConnections.TryRemove(accountId, out var idle))
        {
            try { await idle.DisconnectAsync(ct); } catch { }
            idle.Dispose();
        }
    }

    // Legacy compatibility
    public ImapService GetConnection(Account account) => GetSyncConnection(account);
    public SemaphoreSlim GetLock(string accountId) => GetSyncLock(accountId);

    public void Dispose()
    {
        foreach (var c in _syncConnections.Values) c.Dispose();
        foreach (var c in _idleConnections.Values) c.Dispose();
        foreach (var s in _syncLocks.Values) s.Dispose();
    }
}
