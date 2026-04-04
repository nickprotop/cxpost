using System.Text.Json;
using CXPost.Models;

namespace CXPost.UI.Components;

public static class MessageFormatter
{
    public static string FormatQuotedReply(MailMessage original)
    {
        var lines = new List<string>
        {
            "",
            "",
            $"On {original.Date:MMMM d, yyyy} at {original.Date:h:mm tt}, {original.FromName ?? original.FromAddress} wrote:",
            ""
        };

        var body = GetPlainTextBody(original.BodyPlain);
        if (body != null)
        {
            foreach (var line in body.Split('\n'))
                lines.Add($"> {line.TrimEnd('\r')}");
        }

        return string.Join('\n', lines);
    }

    public static string FormatForwardBody(MailMessage original)
    {
        var lines = new List<string>
        {
            "",
            "",
            "---------- Forwarded message ----------",
            $"From: {original.FromName ?? ""} <{original.FromAddress}>",
            $"Date: {original.Date:MMMM d, yyyy h:mm tt}",
            $"Subject: {original.Subject}",
            $"To: {FormatAddresses(original.ToAddresses)}",
            ""
        };

        var body = GetPlainTextBody(original.BodyPlain);
        if (body != null)
            lines.Add(body);

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Returns true if the body content appears to be HTML.
    /// </summary>
    public static bool IsHtml(string? body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        return body.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<div", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<p>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns plain text from body content, converting HTML via AngleSharp if needed.
    /// </summary>
    public static string? GetPlainTextBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        return IsHtml(body) ? HtmlConverter.ToPlainText(body) : body;
    }

    /// <summary>
    /// Formats a JSON address array as comma-separated plain text.
    /// </summary>
    public static string FormatAddresses(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list != null ? string.Join(", ", list) : json;
        }
        catch
        {
            return json;
        }
    }

    public static string GetReplySubject(string? subject, string prefix = "Re:")
    {
        if (string.IsNullOrEmpty(subject)) return $"{prefix} ";
        if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return subject;
        if (subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)) return subject;
        return $"{prefix} {subject}";
    }

    public static string GetForwardSubject(string? subject, string prefix = "Fwd:")
    {
        if (string.IsNullOrEmpty(subject)) return $"{prefix} ";
        if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return subject;
        if (subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase)) return subject;
        return $"{prefix} {subject}";
    }

    public static string GetBulkForwardSubject(List<MailMessage> messages, string prefix = "Fwd:")
    {
        if (messages.Count == 0) return $"{prefix} ";

        var subjects = messages.Select(m => m.Subject ?? "").Distinct().ToList();
        if (subjects.Count == 1)
            return GetForwardSubject(subjects[0], prefix);

        var first = messages[0].Subject ?? "(no subject)";
        var remaining = messages.Count - 1;
        if (first.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return $"{first} (+{remaining} more)";
        return $"{prefix} {first} (+{remaining} more)";
    }

    public static string FormatBulkForwardBody(string intro, List<MailMessage> messages)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(intro))
        {
            lines.Add(intro);
            lines.Add("");
        }

        foreach (var msg in messages)
        {
            lines.Add("---------- Forwarded message ----------");
            lines.Add($"From: {msg.FromName ?? ""} <{msg.FromAddress}>");
            lines.Add($"Date: {msg.Date:MMMM d, yyyy h:mm tt}");
            lines.Add($"Subject: {msg.Subject}");
            lines.Add($"To: {FormatAddresses(msg.ToAddresses)}");
            lines.Add("");

            var body = GetPlainTextBody(msg.BodyPlain);
            if (body != null)
                lines.Add(body);

            lines.Add("");
        }

        return string.Join('\n', lines);
    }

    public static string GetFolderIcon(FolderType type) => type switch
    {
        FolderType.Inbox => "\U0001f4e5",
        FolderType.Sent => "\U0001f4e4",
        FolderType.Drafts => "\u270f\ufe0f",
        FolderType.Trash => "\U0001f5d1\ufe0f",
        FolderType.Spam => "\u26a0\ufe0f",
        FolderType.Archive => "\U0001f4e6",
        FolderType.Starred => "\u2b50",
        FolderType.Important => "\u2757",
        _ => "\U0001f4c1"
    };

    // Legacy overload for string-based lookups
    public static string GetFolderIcon(string folderName)
    {
        var lower = folderName.ToLowerInvariant();
        if (lower.Contains("inbox")) return GetFolderIcon(FolderType.Inbox);
        if (lower.Contains("sent")) return GetFolderIcon(FolderType.Sent);
        if (lower.Contains("draft")) return GetFolderIcon(FolderType.Drafts);
        if (lower.Contains("trash") || lower.Contains("deleted")) return GetFolderIcon(FolderType.Trash);
        if (lower.Contains("spam") || lower.Contains("junk")) return GetFolderIcon(FolderType.Spam);
        if (lower.Contains("archive") || lower.Contains("all mail")) return GetFolderIcon(FolderType.Archive);
        if (lower.Contains("star") || lower.Contains("flagged")) return GetFolderIcon(FolderType.Starred);
        return GetFolderIcon(FolderType.Other);
    }

}
