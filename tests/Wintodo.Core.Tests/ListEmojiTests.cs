using Wintodo.Core.Models;

namespace Wintodo.Core.Tests;

public sealed class ListEmojiTests
{
    [Theory]
    [InlineData("\U0001F680")]
    [InlineData("\u2764\uFE0F")]
    [InlineData("\U0001F469\u200D\U0001F4BB")]
    [InlineData("\U0001F1FA\U0001F1E6")]
    [InlineData("1\uFE0F\u20E3")]
    public void TryNormalize_AcceptsSingleEmojiGrapheme(string value)
    {
        Assert.True(ListEmoji.TryNormalize(value, out string? normalized));
        Assert.Equal(value, normalized);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("\U0001F680 travel")]
    public void TryNormalize_RejectsTextAndMultipleGraphemes(string value)
    {
        Assert.False(ListEmoji.TryNormalize(value, out string? normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_TreatsBlankAsNoIcon()
    {
        Assert.True(ListEmoji.TryNormalize("  ", out string? normalized));
        Assert.Null(normalized);
    }
}
