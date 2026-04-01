using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using CXPost.Models;

namespace CXPost.UI.Components;

public static partial class MessageFormatter
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
    /// Returns plain text from body content, stripping HTML if needed.
    /// </summary>
    public static string? GetPlainTextBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        return IsHtml(body) ? StripHtmlToPlainText(body) : body;
    }

    /// <summary>
    /// Strips HTML tags and decodes entities to produce plain text.
    /// </summary>
    public static string StripHtmlToPlainText(string html)
    {
        // Remove style and script blocks
        var text = StyleOrScriptRegex().Replace(html, "");
        // Convert <br> and block-level elements to newlines
        text = BrRegex().Replace(text, "\n");
        text = BlockTagRegex().Replace(text, "\n");
        // Remove remaining tags
        text = TagRegex().Replace(text, "");
        // Decode HTML entities
        text = HttpUtility.HtmlDecode(text);
        // Clean up excessive whitespace
        text = ConsecutiveBlankLinesRegex().Replace(text, "\n\n");
        return text.Trim();
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
        return $"{prefix} {subject}";
    }

    [GeneratedRegex(@"<(style|script)[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleOrScriptRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"</(p|div|tr|li|h[1-6]|blockquote)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ConsecutiveBlankLinesRegex();
}
