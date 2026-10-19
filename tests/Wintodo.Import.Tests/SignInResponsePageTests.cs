using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class SignInResponsePageTests
{
    [Theory]
    [InlineData(false, "Signed in", "Did not finish")]
    [InlineData(true, "Did not finish", "Signed in")]
    public void ResponseUsesOutcomeAndSupportsDarkMode(bool failed, string expected, string absent)
    {
        string page = SignInResponsePage.Build(failed, "Local title", "Signed in", "Did not finish");
        Assert.Contains("<title>Local title</title>", page);
        Assert.Contains(expected, page);
        Assert.DoesNotContain(absent, page);
        Assert.Contains("prefers-color-scheme:dark", page);
        Assert.DoesNotContain("src=", page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TitleAndBothMessagesAreHtmlEncoded(bool failed)
    {
        string page = SignInResponsePage.Build(failed, "<title>&", "<script>\"&", "<script>\"&");
        Assert.Contains("<title>&lt;title&gt;&amp;</title>", page);
        Assert.Contains("&lt;script&gt;&quot;&amp;", page);
        Assert.DoesNotContain("<script>", page);
    }
}
