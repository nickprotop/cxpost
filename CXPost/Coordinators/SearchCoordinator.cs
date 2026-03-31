using CXPost.Models;
using CXPost.Services;

namespace CXPost.Coordinators;

public class SearchCoordinator
{
    private readonly IImapService _imap;
    private readonly ICacheService _cache;

    public SearchCoordinator(IImapService imap, ICacheService cache)
    {
        _imap = imap;
        _cache = cache;
    }

    public async Task<List<MailMessage>> SearchAsync(MailFolder folder, string query, CancellationToken ct)
    {
        var matchingUids = await _imap.SearchAsync(folder.Path, query, ct);
        var allMessages = _cache.GetMessages(folder.Id);
        return allMessages.Where(m => matchingUids.Contains(m.Uid)).ToList();
    }
}
