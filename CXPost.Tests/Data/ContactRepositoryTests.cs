using CXPost.Data;
using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Tests.Data;

public class ContactRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ContactRepository _repo;

    public ContactRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DatabaseMigrations.ApplyContacts(_connection);
        _repo = new ContactRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void UpsertContact_creates_new_contact()
    {
        _repo.UpsertContact("alice@example.com", "Alice");
        var results = _repo.Search("alice");

        Assert.Single(results);
        Assert.Equal("alice@example.com", results[0].Address);
        Assert.Equal("Alice", results[0].DisplayName);
    }

    [Fact]
    public void UpsertContact_increments_use_count_on_duplicate()
    {
        _repo.UpsertContact("alice@example.com", "Alice");
        _repo.UpsertContact("alice@example.com", "Alice");
        var results = _repo.Search("alice");

        Assert.Single(results);
        Assert.Equal(2, results[0].UseCount);
    }

    [Fact]
    public void Search_returns_results_ordered_by_use_count_desc()
    {
        _repo.UpsertContact("rare@example.com", "Rare");
        _repo.UpsertContact("frequent@example.com", "Frequent");
        _repo.UpsertContact("frequent@example.com", "Frequent");
        _repo.UpsertContact("frequent@example.com", "Frequent");

        var results = _repo.Search("example.com");

        Assert.Equal(2, results.Count);
        Assert.Equal("frequent@example.com", results[0].Address);
    }

    [Fact]
    public void Search_matches_display_name_and_address()
    {
        _repo.UpsertContact("bob@work.com", "Bob Smith");

        Assert.Single(_repo.Search("bob"));
        Assert.Single(_repo.Search("work.com"));
        Assert.Single(_repo.Search("Smith"));
    }

    [Fact]
    public void Search_returns_empty_for_no_match()
    {
        _repo.UpsertContact("alice@example.com", "Alice");
        Assert.Empty(_repo.Search("nobody"));
    }
}
