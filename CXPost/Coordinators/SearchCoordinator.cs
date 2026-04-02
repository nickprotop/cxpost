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

        var imap = _imapFactory.GetFetchConnection(account);
        var fetchLock = _imapFactory.GetFetchLock(account.Id);
        await fetchLock.WaitAsync(ct);
        try
        {
            await _imapFactory.EnsureConnectedAsync(imap, account, CancellationToken.None);
            var matchingUids = await imap.SearchAsync(folder.Path, query, CancellationToken.None);
            var allMessages = _cache.GetMessages(folder.Id);
            return allMessages.Where(m => matchingUids.Contains(m.Uid)).ToList();
        }
        finally { fetchLock.Release(); }
    }
}
