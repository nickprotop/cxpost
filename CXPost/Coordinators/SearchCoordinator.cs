using CXPost.Models;
using CXPost.Services;

namespace CXPost.Coordinators;

public class SearchCoordinator
{
    private readonly ImapConnectionFactory _imapFactory;
    private readonly ICacheService _cache;
    private readonly IConfigService _configService;

    public SearchCoordinator(ImapConnectionFactory imapFactory, ICacheService cache, IConfigService configService)
    {
        _imapFactory = imapFactory;
        _cache = cache;
        _configService = configService;
    }

    public async Task<List<MailMessage>> SearchAsync(MailFolder folder, string query, CancellationToken ct)
    {
        var account = _configService.Load().Accounts.FirstOrDefault(a => a.Id == folder.AccountId);
        if (account == null) return [];

        var imap = _imapFactory.GetConnection(account);
        var imapLock = _imapFactory.GetLock(account.Id);
        await imapLock.WaitAsync(ct);
        try
        {
            if (!imap.IsConnected)
                await imap.ConnectAsync(account, ct);
            var matchingUids = await imap.SearchAsync(folder.Path, query, ct);
            var allMessages = _cache.GetMessages(folder.Id);
            return allMessages.Where(m => matchingUids.Contains(m.Uid)).ToList();
        }
        finally
        {
            imapLock.Release();
        }
    }
}
