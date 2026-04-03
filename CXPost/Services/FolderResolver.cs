using CXPost.Models;

namespace CXPost.Services;

/// <summary>
/// Centralized folder lookup — resolves special folders by manual override or auto-detected type.
/// </summary>
public static class FolderResolver
{
    public static MailFolder? GetTrash(Account account, ICacheService cache)
    {
        if (!string.IsNullOrEmpty(account.TrashFolderPath))
            return cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == account.TrashFolderPath);
        return cache.GetFolders(account.Id).FirstOrDefault(f => f.FolderType == FolderType.Trash);
    }

    public static MailFolder? GetSent(Account account, ICacheService cache)
    {
        if (!string.IsNullOrEmpty(account.SentFolderPath))
            return cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == account.SentFolderPath);
        return cache.GetFolders(account.Id).FirstOrDefault(f => f.FolderType == FolderType.Sent);
    }

    public static MailFolder? GetDrafts(Account account, ICacheService cache)
    {
        if (!string.IsNullOrEmpty(account.DraftsFolderPath))
            return cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == account.DraftsFolderPath);
        return cache.GetFolders(account.Id).FirstOrDefault(f => f.FolderType == FolderType.Drafts);
    }

    public static MailFolder? GetSpam(Account account, ICacheService cache)
    {
        if (!string.IsNullOrEmpty(account.SpamFolderPath))
            return cache.GetFolders(account.Id).FirstOrDefault(f => f.Path == account.SpamFolderPath);
        return cache.GetFolders(account.Id).FirstOrDefault(f => f.FolderType == FolderType.Spam);
    }
}
