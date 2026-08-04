using System.Globalization;
using System.Text;

namespace Wintodo.Core.Models;

public static class ListEmoji
{
    public const int MaximumUtf16Length = 32;

    public static bool TryNormalize(string? value, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }

        string candidate = value.Trim();
        if (candidate.Length > MaximumUtf16Length
            || !IsSingleTextElement(candidate)
            || !ContainsEmojiSymbol(candidate))
        {
            normalized = null;
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool IsSingleTextElement(string value)
    {
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
        return enumerator.MoveNext() && !enumerator.MoveNext();
    }

    private static bool ContainsEmojiSymbol(string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol
                || rune.Value == 0x20E3)
            {
                return true;
            }
        }

        return false;
    }
}
