using Microsoft.Data.Sqlite;

namespace CXPost.Data;

public static class DatabaseMigrations
{
    public static void Apply(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NOT NULL,
                path TEXT NOT NULL,
                display_name TEXT NOT NULL,
                uidvalidity INTEGER NOT NULL DEFAULT 0,
                unread_count INTEGER NOT NULL DEFAULT 0,
                total_count INTEGER NOT NULL DEFAULT 0,
                UNIQUE(account_id, path)
            );

            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                folder_id INTEGER NOT NULL REFERENCES folders(id),
                uid INTEGER NOT NULL,
                message_id TEXT,
                in_reply_to TEXT,
                "references" TEXT,
                thread_id TEXT,
                from_name TEXT,
                from_address TEXT,
                to_addresses TEXT,
                cc_addresses TEXT,
                subject TEXT,
                date TEXT NOT NULL,
                is_read INTEGER NOT NULL DEFAULT 0,
                is_flagged INTEGER NOT NULL DEFAULT 0,
                has_attachments INTEGER NOT NULL DEFAULT 0,
                body_plain TEXT,
                body_fetched INTEGER NOT NULL DEFAULT 0,
                UNIQUE(folder_id, uid)
            );

            CREATE INDEX IF NOT EXISTS idx_messages_thread ON messages(thread_id);
            CREATE INDEX IF NOT EXISTS idx_messages_date ON messages(folder_id, date DESC);
            CREATE INDEX IF NOT EXISTS idx_messages_from ON messages(from_address);
            """;
        cmd.ExecuteNonQuery();
    }

    public static void ApplyContacts(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS contacts (
                address TEXT PRIMARY KEY,
                display_name TEXT,
                use_count INTEGER NOT NULL DEFAULT 1,
                last_used TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
