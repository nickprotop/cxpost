using CXPost.Data;

namespace CXPost.Services;

public class ContactsService : IContactsService
{
    private readonly ContactRepository _repo;

    public ContactsService(ContactRepository repo)
    {
        _repo = repo;
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
}
