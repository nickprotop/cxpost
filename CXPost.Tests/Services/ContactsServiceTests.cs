using CXPost.Data;
using CXPost.Services;
using Microsoft.Data.Sqlite;

namespace CXPost.Tests.Services;

public class ContactsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ContactsService _service;

    public ContactsServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DatabaseMigrations.ApplyContacts(_connection);
        var repo = new ContactRepository(_connection);
        _service = new ContactsService(repo);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void RecordContact_and_Autocomplete_returns_matches()
    {
        _service.RecordContact("alice@example.com", "Alice");
        var results = _service.Autocomplete("ali");

        Assert.Single(results);
        Assert.Equal("Alice <alice@example.com>", results[0]);
    }

    [Fact]
    public void Autocomplete_formats_without_name_when_null()
    {
        _service.RecordContact("noname@test.com", null);
        var results = _service.Autocomplete("noname");

        Assert.Single(results);
        Assert.Equal("noname@test.com", results[0]);
    }

    [Fact]
    public void Autocomplete_returns_most_used_first()
    {
        _service.RecordContact("rare@test.com", "Rare");
        _service.RecordContact("freq@test.com", "Frequent");
        _service.RecordContact("freq@test.com", "Frequent");
        _service.RecordContact("freq@test.com", "Frequent");

        var results = _service.Autocomplete("test.com");

        Assert.Equal(2, results.Count);
        Assert.StartsWith("Frequent", results[0]);
    }

    [Fact]
    public void GetTopContacts_returns_formatted_top_contacts()
    {
        _service.RecordContact("alice@example.com", "Alice");
        _service.RecordContact("bob@example.com", null);
        _service.RecordContact("alice@example.com", "Alice");

        var results = _service.GetTopContacts(10);

        Assert.Equal(2, results.Count);
        Assert.Equal("Alice <alice@example.com>", results[0]);
        Assert.Equal("bob@example.com", results[1]);
    }
}
