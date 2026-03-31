namespace CXPost.Services;

public interface ICredentialService
{
    string? GetPassword(string accountId);
    void StorePassword(string accountId, string password);
    void DeletePassword(string accountId);
}
