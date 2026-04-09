using CXPost.UI.Components;

namespace CXPost.Tests.UI;

public class HtmlConverterTests
{
    [Fact]
    public void StripsZeroWidthNoBreakSpace_FromOutlookHtml()
    {
        // Outlook/Word frequently emits empty <span>﻿</span> (U+FEFF) wrappers.
        // These are invisible but break terminal rendering when they reach the cell buffer.
        var html = "<html><body><p><span>\uFEFF</span><span>Hello world</span></p></body></html>";
        var result = HtmlConverter.ToMarkup(html);

        Assert.DoesNotContain('\uFEFF', result);
        Assert.Contains("Hello world", result);
    }

    [Fact]
    public void StripsZeroWidthSpace_ButPreservesZwjForEmoji()
    {
        // U+200B (ZWSP) is junk; U+200D (ZWJ) is load-bearing for emoji sequences like 👨‍👩‍👧.
        var html = "<p>a\u200Bb\u200Dc</p>";
        var result = HtmlConverter.ToMarkup(html);

        Assert.DoesNotContain('\u200B', result);
        Assert.Contains('\u200D', result);
    }
}
