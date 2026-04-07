// CXPost/UI/Components/EmailBodyParser.cs
using System.Text.RegularExpressions;

namespace CXPost.UI.Components;

public enum EmailSegmentType
{
    Body,
    Quote,
    Signature
}

public record EmailSegment(EmailSegmentType Type, List<string> Lines);

public static class EmailBodyParser
{
    // Signature trigger patterns (case-insensitive)
    private static readonly string[] SignaturePatterns =
    [
        "-- ",           // RFC 3676 standard
        "--",            // Common variant (exactly "--" on its own line)
        "Sent from my iPhone",
        "Sent from my iPad",
        "Get Outlook for",
        "Best regards,",
        "Kind regards,",
        "Warm regards,",
        "Best,",
        "Thanks,",
        "Regards,",
    ];

    // Quote attribution pattern: "On <date> <name> wrote:"
    private static readonly Regex AttributionRegex = new(
        @"^On\s+.+\s+wrote:\s*$", RegexOptions.Compiled);

    // "-----Original Message-----" pattern
    private static readonly Regex OriginalMessageRegex = new(
        @"^-{3,}\s*Original Message\s*-{3,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses body lines into typed segments.
    /// Lines should already be markup-escaped (for plain text) or converted from HTML.
    /// </summary>
    public static List<EmailSegment> Parse(List<string> lines)
    {
        var segments = new List<EmailSegment>();
        var currentLines = new List<string>();
        var currentType = EmailSegmentType.Body;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Check for signature markers — everything after is signature
            if (IsSignatureStart(trimmed, i, lines.Count))
            {
                // Flush current segment
                if (currentLines.Count > 0)
                    segments.Add(new EmailSegment(currentType, new List<string>(currentLines)));
                currentLines.Clear();

                // Rest of email is signature
                for (int j = i; j < lines.Count; j++)
                    currentLines.Add(lines[j]);
                segments.Add(new EmailSegment(EmailSegmentType.Signature, currentLines));
                return segments;
            }

            // Check for quote patterns
            var isQuote = IsQuoteLine(trimmed);

            // Check for "Original Message" separator or attribution line
            if (!isQuote && (OriginalMessageRegex.IsMatch(trimmed) || AttributionRegex.IsMatch(trimmed)))
                isQuote = true;

            var lineType = isQuote ? EmailSegmentType.Quote : EmailSegmentType.Body;

            if (lineType != currentType && currentLines.Count > 0)
            {
                segments.Add(new EmailSegment(currentType, new List<string>(currentLines)));
                currentLines.Clear();
            }

            currentType = lineType;
            currentLines.Add(line);
        }

        if (currentLines.Count > 0)
            segments.Add(new EmailSegment(currentType, currentLines));

        return segments;
    }

    private static bool IsQuoteLine(string trimmedLine)
    {
        // Standard "> " prefix (or just ">")
        return trimmedLine.StartsWith("> ") || trimmedLine == ">";
    }

    private static bool IsSignatureStart(string trimmedLine, int lineIndex, int totalLines)
    {
        // Only trigger if remaining lines are short (< 8 lines) — avoids false positives
        int remainingLines = totalLines - lineIndex;
        if (remainingLines > 10) return false;

        foreach (var pattern in SignaturePatterns)
        {
            if (pattern == "-- " || pattern == "--")
            {
                // Exact match for delimiter patterns
                if (trimmedLine == pattern) return true;
            }
            else
            {
                // StartsWith for phrase patterns
                if (trimmedLine.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
