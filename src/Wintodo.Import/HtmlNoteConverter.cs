using System.Net;
using System.Text.RegularExpressions;

namespace Wintodo.Import;

/// <summary>Turns an HTML note body into the plain text the detail pane edits.</summary>
public static partial class HtmlNoteConverter
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string text = HiddenBlocks().Replace(html, string.Empty);
        text = LineBreaks().Replace(text, "\n");
        text = BlockEnds().Replace(text, "\n");
        text = ListItems().Replace(text, "\n• ");
        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text)
            .Replace(' ', ' ')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        text = TrailingSpaces().Replace(text, "\n");
        text = ExtraBlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<(head|style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenBlocks();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"</(p|div|tr|h[1-6]|ul|ol|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnds();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItems();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
