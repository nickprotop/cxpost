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

        if (original.BodyPlain != null)
        {
            foreach (var line in original.BodyPlain.Split('\n'))
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
            $"To: {original.ToAddresses}",
            ""
        };

        if (original.BodyPlain != null)
            lines.Add(original.BodyPlain);

        return string.Join('\n', lines);
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
        return $"{prefix} {subject}";
    }
}
