using CXPost.UI.Dialogs;

namespace CXPost.Tests.UI;

public class AutocompleteTokenTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("jo", "jo")]
    [InlineData("alice@example.com, jo", "jo")]
    [InlineData("alice@example.com, bob@test.com, jo", "jo")]
    [InlineData("alice@example.com, ", "")]
    [InlineData("  jo  ", "jo")]
    [InlineData("alice@example.com,jo", "jo")]
    [InlineData("John Doe <john@example.com>, ali", "ali")]
    public void ExtractCurrentToken_returns_text_after_last_comma_trimmed(string input, string expected)
    {
        var result = ComposeDialog.ExtractCurrentToken(input);
        Assert.Equal(expected, result);
    }
}
