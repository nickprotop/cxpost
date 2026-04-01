using System.Text;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using SharpConsoleUI.Parsing;

namespace CXPost.UI.Components;

/// <summary>
/// Converts HTML email body to ConsoleEx markup for terminal rendering.
/// Handles common email HTML: formatting, links, lists, headings, blockquotes, tables.
/// </summary>
public static class HtmlToMarkup
{
    public static string Convert(string html)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = parser.ParseDocument(html);

        var sb = new StringBuilder();
        var state = new ConvertState();

        ProcessNode(document.Body ?? (INode)document.DocumentElement, sb, state, 50);

        // Aggressively clean up blank lines — email HTML is full of spacers
        var lines = sb.ToString().Split('\n');
        var cleaned = new List<string>();
        var consecutiveEmpty = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            // Strip markup to check if line is visually empty
            var stripped = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\[/?[^\]]*\]", "").Trim();

            if (string.IsNullOrEmpty(stripped))
            {
                consecutiveEmpty++;
                if (consecutiveEmpty <= 1) // Allow at most 1 blank line
                    cleaned.Add("");
            }
            else
            {
                consecutiveEmpty = 0;
                cleaned.Add(trimmed);
            }
        }

        // Trim leading/trailing blanks
        while (cleaned.Count > 0 && string.IsNullOrEmpty(cleaned[0]))
            cleaned.RemoveAt(0);
        while (cleaned.Count > 0 && string.IsNullOrEmpty(cleaned[^1]))
            cleaned.RemoveAt(cleaned.Count - 1);

        return string.Join('\n', cleaned);
    }

    private class ConvertState
    {
        public int IndentLevel;
        public bool InPre;
        public int ListDepth;
        public int OrderedCounter;
        public bool LastWasBlock;
    }

    private static void ProcessNode(INode node, StringBuilder sb, ConvertState state, int depth = 50)
    {
        if (depth <= 0) return;

        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    ProcessText(text, sb, state);
                    break;
                case IHtmlElement element:
                    ProcessElement(element, sb, state, depth - 1);
                    break;
            }
        }
    }

    private static void ProcessText(IText text, StringBuilder sb, ConvertState state)
    {
        var content = text.Data;

        if (state.InPre)
        {
            sb.Append(MarkupParser.Escape(content));
            return;
        }

        // Collapse whitespace like a browser
        content = CollapseWhitespace(content);
        if (string.IsNullOrEmpty(content)) return;

        sb.Append(MarkupParser.Escape(content));
        state.LastWasBlock = false;
    }

    private static void ProcessElement(IHtmlElement element, StringBuilder sb, ConvertState state, int depth = 50)
    {
        var tag = element.TagName.ToLowerInvariant();

        // Skip invisible/irrelevant elements
        if (tag is "style" or "script" or "head" or "meta" or "link" or "title" or "noscript")
            return;

        // Handle display:none and hidden spacers
        var style = element.GetAttribute("style") ?? "";
        if (style.Contains("display:none") || style.Contains("display: none"))
            return;
        if (style.Contains("font-size:0") || style.Contains("font-size: 0"))
            return;

        // Skip 1px spacer images and tracking pixels
        if (tag == "img")
        {
            var width = element.GetAttribute("width");
            var height = element.GetAttribute("height");
            if (width == "1" || height == "1") return;
        }

        // Skip empty elements (common in email templates)
        if (tag is "div" or "p" or "span" or "td")
        {
            var text = element.TextContent.Trim();
            var innerHtml = element.InnerHtml.Trim();
            if (string.IsNullOrEmpty(text) && !innerHtml.Contains("<img") && !innerHtml.Contains("<a "))
            {
                // Check if it's just &nbsp; or whitespace
                if (string.IsNullOrWhiteSpace(text.Replace("\u00A0", "")))
                    return;
            }
        }

        switch (tag)
        {
            // Block elements — ensure newline before
            case "p":
                EnsureBlankLine(sb, state);
                ProcessNode(element, sb, state, depth);
                EnsureBlankLine(sb, state);
                break;

            case "div":
            case "section":
            case "article":
            case "main":
            case "header":
            case "footer":
                EnsureNewline(sb, state);
                ProcessNode(element, sb, state, depth);
                EnsureNewline(sb, state);
                break;

            case "br":
                sb.AppendLine();
                state.LastWasBlock = true;
                break;

            case "hr":
                EnsureNewline(sb, state);
                sb.AppendLine($"[grey50]{"─".PadRight(60, '─')}[/]");
                state.LastWasBlock = true;
                break;

            // Headings
            case "h1":
                EnsureBlankLine(sb, state);
                sb.Append("[bold cyan1]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                EnsureBlankLine(sb, state);
                break;

            case "h2":
                EnsureBlankLine(sb, state);
                sb.Append("[bold white]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                EnsureBlankLine(sb, state);
                break;

            case "h3":
            case "h4":
            case "h5":
            case "h6":
                EnsureBlankLine(sb, state);
                sb.Append("[bold grey93]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                EnsureNewline(sb, state);
                break;

            // Inline formatting
            case "b":
            case "strong":
                sb.Append("[bold]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                break;

            case "i":
            case "em":
                sb.Append("[italic]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                break;

            case "u":
            case "ins":
                sb.Append("[underline]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                break;

            case "s":
            case "strike":
            case "del":
                sb.Append("[strikethrough]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                break;

            case "code":
                sb.Append("[grey70 on grey19]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                break;

            case "mark":
                sb.Append("[black on yellow]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                break;

            // Links
            case "a":
                var href = element.GetAttribute("href");
                sb.Append("[underline cyan1]");
                ProcessNode(element, sb, state, depth);
                sb.Append("[/]");
                if (!string.IsNullOrEmpty(href) && !href.StartsWith("#") && !href.StartsWith("mailto:"))
                {
                    var linkText = element.TextContent.Trim();
                    if (linkText != href) // Don't duplicate if link text IS the URL
                        sb.Append($" [grey50]({MarkupParser.Escape(href)})[/]");
                }
                break;

            // Images — show alt text
            case "img":
                var alt = element.GetAttribute("alt");
                if (!string.IsNullOrWhiteSpace(alt))
                    sb.Append($"[grey50][[IMG: {MarkupParser.Escape(alt)}]][/]");
                break;

            // Lists
            case "ul":
                EnsureNewline(sb, state);
                state.ListDepth++;
                ProcessNode(element, sb, state, depth);
                state.ListDepth--;
                if (state.ListDepth == 0) EnsureNewline(sb, state);
                break;

            case "ol":
                EnsureNewline(sb, state);
                state.ListDepth++;
                var prevCounter = state.OrderedCounter;
                state.OrderedCounter = 0;
                ProcessNode(element, sb, state, depth);
                state.OrderedCounter = prevCounter;
                state.ListDepth--;
                if (state.ListDepth == 0) EnsureNewline(sb, state);
                break;

            case "li":
                var indent = new string(' ', (state.ListDepth - 1) * 2 + 2);
                var parent = element.ParentElement?.TagName.ToLowerInvariant();
                if (parent == "ol")
                {
                    state.OrderedCounter++;
                    sb.Append($"{indent}[grey70]{state.OrderedCounter}.[/] ");
                }
                else
                {
                    sb.Append($"{indent}[grey70]•[/] ");
                }
                ProcessNode(element, sb, state, depth);
                EnsureNewline(sb, state);
                break;

            // Blockquote
            case "blockquote":
                EnsureNewline(sb, state);
                state.IndentLevel++;
                var quoteContent = new StringBuilder();
                ProcessNode(element, quoteContent, state, depth);
                state.IndentLevel--;

                foreach (var line in quoteContent.ToString().Split('\n'))
                {
                    var prefix = new string(' ', state.IndentLevel * 2);
                    sb.AppendLine($"{prefix}[grey50]▎[/] [italic grey70]{MarkupParser.Escape(line.Trim())}[/]");
                }
                state.LastWasBlock = true;
                break;

            // Preformatted
            case "pre":
                EnsureNewline(sb, state);
                sb.Append("[grey70 on grey11]");
                state.InPre = true;
                ProcessNode(element, sb, state, depth);
                state.InPre = false;
                sb.Append("[/]");
                EnsureNewline(sb, state);
                break;

            // Table — simple text rendering
            case "table":
                EnsureNewline(sb, state);
                ProcessNode(element, sb, state, depth);
                EnsureNewline(sb, state);
                break;

            case "tr":
                ProcessTableRow(element, sb, state, depth);
                break;

            case "td":
            case "th":
                // Handled by ProcessTableRow
                break;

            // Span and other inline containers
            default:
                ProcessNode(element, sb, state, depth);
                break;
        }
    }

    private static void ProcessTableRow(IHtmlElement tr, StringBuilder sb, ConvertState state, int depth = 50)
    {
        var cells = tr.Children
            .Where(c => c.TagName.ToLowerInvariant() is "td" or "th")
            .ToList();

        if (cells.Count == 0) return;

        var isHeader = cells.Any(c => c.TagName.ToLowerInvariant() == "th");
        var parts = new List<string>();

        foreach (var cell in cells)
        {
            var cellSb = new StringBuilder();
            ProcessNode(cell, cellSb, state, depth);
            var text = cellSb.ToString().Trim();
            parts.Add(text);
        }

        var row = string.Join("  │  ", parts);
        if (isHeader)
        {
            sb.AppendLine($"[bold]{row}[/]");
            sb.AppendLine($"[grey50]{new string('─', row.Length)}[/]");
        }
        else
        {
            sb.AppendLine(row);
        }

        state.LastWasBlock = true;
    }

    private static void EnsureNewline(StringBuilder sb, ConvertState state)
    {
        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.AppendLine();
        }
        state.LastWasBlock = true;
    }

    private static void EnsureBlankLine(StringBuilder sb, ConvertState state)
    {
        EnsureNewline(sb, state);
        if (sb.Length >= 2 && sb[^2] != '\n')
        {
            sb.AppendLine();
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString();
    }
}
