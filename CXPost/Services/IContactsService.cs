namespace CXPost.Services;

public interface IContactsService
{
    void RecordContact(string address, string? displayName);
    List<string> Autocomplete(string query);
}
