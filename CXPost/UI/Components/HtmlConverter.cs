using System.Text;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using SharpConsoleUI.Parsing;

namespace CXPost.UI.Components;

public enum HtmlOutputMode { Markup, PlainText }

/// <summary>
/// Converts HTML email body to either ConsoleEx markup (for display) or plain text (for compose/reply).
/// Uses AngleSharp for proper DOM parsing. Shared DOM walker with mode-dependent output.
/// </summary>
public static class HtmlConverter
{
    public static string ToMarkup(string html) => Convert(html, HtmlOutputMode.Markup);
    public static string ToPlainText(string html) => Convert(html, HtmlOutputMode.PlainText);

    private static string Convert(string html, HtmlOutputMode mode)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = parser.ParseDocument(html);

        var sb = new StringBuilder();
        var state = new ConvertState { Mode = mode };

        ProcessNode(document.Body ?? (INode)document.DocumentElement, sb, state, 50);

        // Clean up blank lines
        var lines = sb.ToString().Split('\n');
        var cleaned = new List<string>();
        var consecutiveEmpty = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            var stripped = mode == HtmlOutputMode.Markup
                ? System.Text.RegularExpressions.Regex.Replace(trimmed, @"\[/?[^\]]*\]", "").Trim()
                : trimmed.Trim();

            if (string.IsNullOrEmpty(stripped))
            {
                consecutiveEmpty++;
                if (consecutiveEmpty <= 1)
                    cleaned.Add("");
            }
            else
            {
                consecutiveEmpty = 0;
                cleaned.Add(trimmed);
            }
        }

        while (cleaned.Count > 0 && string.IsNullOrEmpty(cleaned[0]))
            cleaned.RemoveAt(0);
        while (cleaned.Count > 0 && string.IsNullOrEmpty(cleaned[^1]))
            cleaned.RemoveAt(cleaned.Count - 1);

        return string.Join('\n', cleaned);
    }

    private class ConvertState
    {
        public HtmlOutputMode Mode;
        public int IndentLevel;
        public bool InPre;
        public int ListDepth;
        public int OrderedCounter;
        public bool LastWasBlock;
    }

    private static string Escape(string text, ConvertState state)
        => state.Mode == HtmlOutputMode.Markup ? MarkupParser.Escape(text) : text;

    private static string Wrap(string text, string markupTag, ConvertState state)
        => state.Mode == HtmlOutputMode.Markup ? $"[{markupTag}]{text}[/]" : text;

    private static void AppendMarkup(StringBuilder sb, string markup, ConvertState state)
    {
        if (state.Mode == HtmlOutputMode.Markup)
            sb.Append(markup);
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
            sb.Append(Escape(content, state));
            return;
        }

        content = CollapseWhitespace(content);
        if (string.IsNullOrEmpty(content)) return;

        sb.Append(Escape(content, state));
        state.LastWasBlock = false;
    }

    private static void ProcessElement(IHtmlElement element, StringBuilder sb, ConvertState state, int depth = 50)
    {
        var tag = element.TagName.ToLowerInvariant();

        if (tag is "style" or "script" or "head" or "meta" or "link" or "title" or "noscript")
            return;

        var style = element.GetAttribute("style") ?? "";
        if (style.Contains("display:none") || style.Contains("display: none"))
            return;
        if (style.Contains("font-size:0") || style.Contains("font-size: 0"))
            return;

        if (tag == "img")
        {
            var width = element.GetAttribute("width");
            var height = element.GetAttribute("height");
            if (width == "1" || height == "1") return;
        }

        if (tag is "div" or "p" or "span" or "td")
        {
            var text = element.TextContent.Trim();
            var innerHtml = element.InnerHtml.Trim();
            if (string.IsNullOrEmpty(text) && !innerHtml.Contains("<img") && !innerHtml.Contains("<a "))
            {
                if (string.IsNullOrWhiteSpace(text.Replace("\u00A0", "")))
                    return;
            }
        }

        switch (tag)
        {
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
                if (state.Mode == HtmlOutputMode.Markup)
                    sb.AppendLine("[grey50]" + "─".PadRight(60, '─') + "[/]");
                else
                    sb.AppendLine(new string('-', 60));
                state.LastWasBlock = true;
                break;

            case "h1":
                EnsureBlankLine(sb, state);
                AppendMarkup(sb, "[bold cyan1]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                EnsureBlankLine(sb, state);
                break;

            case "h2":
                EnsureBlankLine(sb, state);
                AppendMarkup(sb, "[bold white]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                EnsureBlankLine(sb, state);
                break;

            case "h3":
            case "h4":
            case "h5":
            case "h6":
                EnsureBlankLine(sb, state);
                AppendMarkup(sb, "[bold grey93]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                EnsureNewline(sb, state);
                break;

            case "b":
            case "strong":
                AppendMarkup(sb, "[bold]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                break;

            case "i":
            case "em":
                AppendMarkup(sb, "[italic]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                break;

            case "u":
            case "ins":
                AppendMarkup(sb, "[underline]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                break;

            case "s":
            case "strike":
            case "del":
                AppendMarkup(sb, "[strikethrough]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                break;

            case "code":
                AppendMarkup(sb, "[grey70 on grey19]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                break;

            case "mark":
                AppendMarkup(sb, "[black on yellow]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                break;

            case "a":
                var href = element.GetAttribute("href");
                AppendMarkup(sb, "[underline cyan1]", state);
                ProcessNode(element, sb, state, depth);
                AppendMarkup(sb, "[/]", state);
                if (!string.IsNullOrEmpty(href) && !href.StartsWith("#") && !href.StartsWith("mailto:"))
                {
                    var linkText = element.TextContent.Trim();
                    if (linkText != href)
                    {
                        if (state.Mode == HtmlOutputMode.Markup)
                            sb.Append($" [grey50]({MarkupParser.Escape(href)})[/]");
                        else
                            sb.Append($" ({href})");
                    }
                }
                break;

            case "img":
                var alt = element.GetAttribute("alt");
                if (!string.IsNullOrWhiteSpace(alt))
                {
                    if (state.Mode == HtmlOutputMode.Markup)
                        sb.Append($"[grey50][[IMG: {MarkupParser.Escape(alt)}]][/]");
                    else
                        sb.Append($"[IMG: {alt}]");
                }
                break;

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
                    if (state.Mode == HtmlOutputMode.Markup)
                        sb.Append($"{indent}[grey70]{state.OrderedCounter}.[/] ");
                    else
                        sb.Append($"{indent}{state.OrderedCounter}. ");
                }
                else
                {
                    if (state.Mode == HtmlOutputMode.Markup)
                        sb.Append($"{indent}[grey70]\u2022[/] ");
                    else
                        sb.Append($"{indent}\u2022 ");
                }
                ProcessNode(element, sb, state, depth);
                EnsureNewline(sb, state);
                break;

            case "blockquote":
                EnsureNewline(sb, state);
                state.IndentLevel++;
                var quoteContent = new StringBuilder();
                ProcessNode(element, quoteContent, state, depth);
                state.IndentLevel--;

                foreach (var line in quoteContent.ToString().Split('\n'))
                {
                    var prefix = new string(' ', state.IndentLevel * 2);
                    if (state.Mode == HtmlOutputMode.Markup)
                        sb.AppendLine($"{prefix}[grey50]\u258e[/] [italic grey70]{MarkupParser.Escape(line.Trim())}[/]");
                    else
                        sb.AppendLine($"{prefix}> {line.Trim()}");
                }
                state.LastWasBlock = true;
                break;

            case "pre":
                EnsureNewline(sb, state);
                AppendMarkup(sb, "[grey70 on grey11]", state);
                state.InPre = true;
                ProcessNode(element, sb, state, depth);
                state.InPre = false;
                AppendMarkup(sb, "[/]", state);
                EnsureNewline(sb, state);
                break;

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
                break;

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

        var row = string.Join("  |  ", parts);
        if (isHeader)
        {
            if (state.Mode == HtmlOutputMode.Markup)
            {
                sb.AppendLine($"[bold]{row}[/]");
                sb.AppendLine($"[grey50]{new string('\u2500', row.Length)}[/]");
            }
            else
            {
                sb.AppendLine(row);
                sb.AppendLine(new string('-', row.Length));
            }
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
            sb.AppendLine();
        state.LastWasBlock = true;
    }

    private static void EnsureBlankLine(StringBuilder sb, ConvertState state)
    {
        EnsureNewline(sb, state);
        if (sb.Length >= 2 && sb[^2] != '\n')
            sb.AppendLine();
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
