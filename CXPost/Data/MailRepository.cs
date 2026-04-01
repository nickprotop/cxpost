using CXPost.Models;
using Microsoft.Data.Sqlite;

namespace CXPost.Data;

public class MailRepository
{
    private readonly SqliteConnection _connection;

    public MailRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public void UpsertFolder(MailFolder folder)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO folders (account_id, path, display_name, uidvalidity, unread_count, total_count)
            VALUES (@accountId, @path, @displayName, @uidvalidity, @unreadCount, @totalCount)
            ON CONFLICT(account_id, path) DO UPDATE SET
                display_name = @displayName,
                uidvalidity = @uidvalidity,
                unread_count = @unreadCount,
                total_count = @totalCount
            """;
        cmd.Parameters.AddWithValue("@accountId", folder.AccountId);
        cmd.Parameters.AddWithValue("@path", folder.Path);
        cmd.Parameters.AddWithValue("@displayName", folder.DisplayName);
        cmd.Parameters.AddWithValue("@uidvalidity", folder.UidValidity);
        cmd.Parameters.AddWithValue("@unreadCount", folder.UnreadCount);
        cmd.Parameters.AddWithValue("@totalCount", folder.TotalCount);
        cmd.ExecuteNonQuery();
    }

    public List<MailFolder> GetFolders(string accountId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, account_id, path, display_name, uidvalidity, unread_count, total_count FROM folders WHERE account_id = @accountId";
        cmd.Parameters.AddWithValue("@accountId", accountId);

        var folders = new List<MailFolder>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new MailFolder
            {
                Id = reader.GetInt32(0),
                AccountId = reader.GetString(1),
                Path = reader.GetString(2),
                DisplayName = reader.GetString(3),
                UidValidity = (uint)reader.GetInt64(4),
                UnreadCount = reader.GetInt32(5),
                TotalCount = reader.GetInt32(6)
            });
        }
        return folders;
    }

    public void UpsertMessage(MailMessage msg)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO messages (folder_id, uid, message_id, in_reply_to, "references", thread_id,
                from_name, from_address, to_addresses, cc_addresses, subject, date,
                is_read, is_flagged, has_attachments, body_plain, body_fetched)
            VALUES (@folderId, @uid, @messageId, @inReplyTo, @references, @threadId,
                @fromName, @fromAddress, @toAddresses, @ccAddresses, @subject, @date,
                @isRead, @isFlagged, @hasAttachments, @bodyPlain, @bodyFetched)
            ON CONFLICT(folder_id, uid) DO UPDATE SET
                message_id = @messageId, in_reply_to = @inReplyTo, "references" = @references,
                thread_id = @threadId, from_name = @fromName, from_address = @fromAddress,
                to_addresses = @toAddresses, cc_addresses = @ccAddresses, subject = @subject,
                date = @date, is_read = @isRead, is_flagged = @isFlagged,
                has_attachments = @hasAttachments
            """;
        cmd.Parameters.AddWithValue("@folderId", msg.FolderId);
        cmd.Parameters.AddWithValue("@uid", (long)msg.Uid);
        cmd.Parameters.AddWithValue("@messageId", (object?)msg.MessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inReplyTo", (object?)msg.InReplyTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@references", (object?)msg.References ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@threadId", (object?)msg.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromName", (object?)msg.FromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromAddress", (object?)msg.FromAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@toAddresses", (object?)msg.ToAddresses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ccAddresses", (object?)msg.CcAddresses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)msg.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date", msg.Date.ToString("O"));
        cmd.Parameters.AddWithValue("@isRead", msg.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@isFlagged", msg.IsFlagged ? 1 : 0);
        cmd.Parameters.AddWithValue("@hasAttachments", msg.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@bodyPlain", (object?)msg.BodyPlain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyFetched", msg.BodyFetched ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public List<MailMessage> GetMessages(int folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, folder_id, uid, message_id, in_reply_to, "references", thread_id,
                from_name, from_address, to_addresses, cc_addresses, subject, date,
                is_read, is_flagged, has_attachments, body_plain, body_fetched
            FROM messages WHERE folder_id = @folderId ORDER BY date DESC
            """;
        cmd.Parameters.AddWithValue("@folderId", folderId);

        var messages = new List<MailMessage>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(ReadMessage(reader));
        }
        return messages;
    }

    public void UpdateMessageFlags(int folderId, uint uid, bool isRead, bool isFlagged)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE messages SET is_read = @isRead, is_flagged = @isFlagged WHERE folder_id = @folderId AND uid = @uid";
        cmd.Parameters.AddWithValue("@isRead", isRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@isFlagged", isFlagged ? 1 : 0);
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        cmd.ExecuteNonQuery();
    }

    public void DeleteMessage(int folderId, uint uid)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE folder_id = @folderId AND uid = @uid";
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        cmd.ExecuteNonQuery();
    }

    public string? GetMessageBody(int folderId, uint uid)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT body_plain FROM messages WHERE folder_id = @folderId AND uid = @uid AND body_fetched = 1";
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        return cmd.ExecuteScalar() as string;
    }

    public void StoreMessageBody(int folderId, uint uid, string body)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE messages SET body_plain = @body, body_fetched = 1 WHERE folder_id = @folderId AND uid = @uid";
        cmd.Parameters.AddWithValue("@body", body);
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.Parameters.AddWithValue("@uid", (long)uid);
        cmd.ExecuteNonQuery();
    }

    public HashSet<uint> GetUids(int folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT uid FROM messages WHERE folder_id = @folderId";
        cmd.Parameters.AddWithValue("@folderId", folderId);

        var uids = new HashSet<uint>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            uids.Add((uint)reader.GetInt64(0));
        return uids;
    }

    public void PurgeFolderMessages(int folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE folder_id = @folderId";
        cmd.Parameters.AddWithValue("@folderId", folderId);
        cmd.ExecuteNonQuery();
    }

    private static MailMessage ReadMessage(SqliteDataReader reader)
    {
        return new MailMessage
        {
            Id = reader.GetInt32(0),
            FolderId = reader.GetInt32(1),
            Uid = (uint)reader.GetInt64(2),
            MessageId = reader.IsDBNull(3) ? null : reader.GetString(3),
            InReplyTo = reader.IsDBNull(4) ? null : reader.GetString(4),
            References = reader.IsDBNull(5) ? null : reader.GetString(5),
            ThreadId = reader.IsDBNull(6) ? null : reader.GetString(6),
            FromName = reader.IsDBNull(7) ? null : reader.GetString(7),
            FromAddress = reader.IsDBNull(8) ? null : reader.GetString(8),
            ToAddresses = reader.IsDBNull(9) ? null : reader.GetString(9),
            CcAddresses = reader.IsDBNull(10) ? null : reader.GetString(10),
            Subject = reader.IsDBNull(11) ? null : reader.GetString(11),
            Date = DateTime.Parse(reader.GetString(12), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            IsRead = reader.GetInt32(13) != 0,
            IsFlagged = reader.GetInt32(14) != 0,
            HasAttachments = reader.GetInt32(15) != 0,
            BodyPlain = reader.IsDBNull(16) ? null : reader.GetString(16),
            BodyFetched = reader.GetInt32(17) != 0
        };
    }
}
