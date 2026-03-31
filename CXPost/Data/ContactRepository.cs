using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Data;

public class ContactRepository
{
    private readonly SqliteConnection _connection;

    public ContactRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public void UpsertContact(string address, string? displayName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO contacts (address, display_name, use_count, last_used)
            VALUES (@address, @displayName, 1, @lastUsed)
            ON CONFLICT(address) DO UPDATE SET
                display_name = COALESCE(@displayName, display_name),
                use_count = use_count + 1,
                last_used = @lastUsed
            """;
        cmd.Parameters.AddWithValue("@address", address);
        cmd.Parameters.AddWithValue("@displayName", (object?)displayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastUsed", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<Contact> Search(string query)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT address, display_name, use_count, last_used
            FROM contacts
            WHERE address LIKE @query OR display_name LIKE @query
            ORDER BY use_count DESC
            LIMIT 20
            """;
        cmd.Parameters.AddWithValue("@query", $"%{query}%");

        var contacts = new List<Contact>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            contacts.Add(new Contact
            {
                Address = reader.GetString(0),
                DisplayName = reader.IsDBNull(1) ? null : reader.GetString(1),
                UseCount = reader.GetInt32(2),
                LastUsed = DateTime.Parse(reader.GetString(3))
            });
        }
        return contacts;
    }
}
