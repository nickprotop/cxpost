using System.Text.Json;
using CXPost.Data;
using CXPost.Models;

namespace CXPost.Services;

public class ContactsService : IContactsService
{
    private readonly ContactRepository _repo;

    public ContactsService(ContactRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Populates the contacts database from cached message headers.
    /// Imports From addresses from all folders and To/Cc from Sent folders.
    /// Uses batch transaction for performance.
    /// </summary>
    public void ImportFromCache(ICacheService cache, List<Account> accounts)
    {
        var batch = new List<(string address, string? displayName)>();
        var accountEmails = new HashSet<string>(accounts.Select(a => a.Email), StringComparer.OrdinalIgnoreCase);

        foreach (var account in accounts)
            CollectContacts(cache, account, accountEmails, batch);

        if (batch.Count > 0)
            _repo.UpsertBatch(batch);
    }

    /// <summary>
    /// Imports contacts from a single account's cached messages. Call after sync.
    /// </summary>
    public void ImportFromCache(ICacheService cache, Account account)
    {
        var batch = new List<(string address, string? displayName)>();
        var accountEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { account.Email };
        CollectContacts(cache, account, accountEmails, batch);

        if (batch.Count > 0)
            _repo.UpsertBatch(batch);
    }

    private void CollectContacts(ICacheService cache, Account account,
        HashSet<string> accountEmails, List<(string address, string? displayName)> batch)
    {
        var folders = cache.GetFolders(account.Id);
        foreach (var folder in folders)
        {
            var messages = cache.GetMessages(folder.Id);
            foreach (var msg in messages)
            {
                if (!string.IsNullOrEmpty(msg.FromAddress) && !accountEmails.Contains(msg.FromAddress))
                    batch.Add((msg.FromAddress, msg.FromName));

                if (folder.FolderType == FolderType.Sent)
                {
                    ParseAddressJson(msg.ToAddresses, batch);
                    ParseAddressJson(msg.CcAddresses, batch);
                }
            }
        }
    }

    private static void ParseAddressJson(string? json, List<(string address, string? displayName)> batch)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var addresses = JsonSerializer.Deserialize<List<string>>(json);
            if (addresses == null) return;
            foreach (var addr in addresses)
                if (!string.IsNullOrEmpty(addr))
                    batch.Add((addr, null));
        }
        catch { }
    }

    public void RecordContact(string address, string? displayName)
    {
        _repo.UpsertContact(address, displayName);
    }

    public List<string> Autocomplete(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var contacts = _repo.Search(query);
        return contacts.Select(c =>
            string.IsNullOrEmpty(c.DisplayName)
                ? c.Address
                : $"{c.DisplayName} <{c.Address}>")
            .ToList();
    }

    public List<string> GetTopContacts(int limit = 10)
    {
        var contacts = _repo.GetTop(limit);
        return contacts.Select(c =>
            string.IsNullOrEmpty(c.DisplayName)
                ? c.Address
                : $"{c.DisplayName} <{c.Address}>")
            .ToList();
    }
}
