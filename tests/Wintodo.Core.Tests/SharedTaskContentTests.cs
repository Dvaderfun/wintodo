using Wintodo.Core.Presentation;

namespace Wintodo.Core.Tests;

public sealed class SharedTaskContentTests
{
    [Theory]
    [InlineData("Buy milk", null, null, "Buy milk", null)]
    [InlineData("\r\n  Café ☕\r\nKeep українська\r\n\r\n  indentation  \r\n", null, null, "Café ☕", "Keep українська\n\n  indentation  \n")]
    [InlineData("\rFirst\rSecond", null, null, "First", "Second")]
    [InlineData("https://example.test/a?q=1#x", null, "Ignored metadata", "https://example.test/a?q=1#x", "https://example.test/a?q=1#x")]
    [InlineData("https://example.test/a\nMore", null, null, "https://example.test/a", "More\nhttps://example.test/a")]
    [InlineData(null, "https://example.test/a", "Source title", "Source title", "https://example.test/a")]
    [InlineData(null, "https://example.test/a", " ", "https://example.test/a", "https://example.test/a")]
    [InlineData("Text title\nDetails", "https://example.test/a", null, "Text title", "Text title\nDetails\nhttps://example.test/a")]
    [InlineData("Text title\nhttps://example.test/a", "https://example.test/a", "Source title", "Source title", "Text title\nhttps://example.test/a")]
    [InlineData("https://example.test/a", "https://example.test/a", null, "https://example.test/a", "https://example.test/a")]
    [InlineData("keep\nhttps://EXAMPLE.test/a", "https://example.test/a", "Name", "Name", "keep\nhttps://EXAMPLE.test/a\nhttps://example.test/a")]
    public void SupportedPayload_PreservesTitleAndNote(string? text, string? link, string? sourceTitle, string title, string? note)
    {
        Assert.True(SharedTaskContent.TryMap(text, link, sourceTitle, out var content));
        Assert.Equal(new SharedTaskContent(title, note), content);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" \r\n ", null)]
    [InlineData("Fallback", "file:///C:/a")]
    [InlineData("Fallback", "javascript:alert(1)")]
    [InlineData(null, "not a URL")]
    public void EmptyOrUnsupportedPayload_IsRejected(string? text, string? link)
    {
        Assert.False(SharedTaskContent.TryMap(text, link, "Source", out var content));
        Assert.Null(content);
    }

    [Fact]
    public void LongText_IsNotTruncated()
    {
        string title = new('x', 10000);
        string note = new('y', 100000);
        Assert.True(SharedTaskContent.TryMap(title + "\n" + note, null, null, out var content));
        Assert.Equal(new SharedTaskContent(title, note), content);
    }
}
