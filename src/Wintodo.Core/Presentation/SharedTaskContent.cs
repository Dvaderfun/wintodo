namespace Wintodo.Core.Presentation;

/// <summary>Local, lossless title/note policy for the Windows Text/WebLink contract.</summary>
public sealed record SharedTaskContent(string Title, string? Note)
{
    public static bool TryMap(string? text, string? webLink, string? sourceTitle,
        out SharedTaskContent? content)
    {
        content = null;
        string normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        string? title = first < 0 ? null : lines[first].Trim();
        string? note;
        if (webLink is not null)
        {
            if (!IsHttpUrl(webLink)) return false;
            title = !string.IsNullOrWhiteSpace(sourceTitle) ? sourceTitle.Trim() : title ?? webLink;
            note = normalized;
            if (!lines.Contains(webLink, StringComparer.Ordinal))
                note = string.IsNullOrEmpty(note) ? webLink : note + (note.EndsWith('\n') ? "" : "\n") + webLink;
        }
        else
        {
            if (title is null) return false;
            note = string.Join('\n', lines.Skip(first + 1));
            if (IsHttpUrl(title) && !note.Split('\n').Contains(title, StringComparer.Ordinal))
                note = string.IsNullOrEmpty(note) ? title : note + (note.EndsWith('\n') ? "" : "\n") + title;
        }
        content = new SharedTaskContent(title, string.IsNullOrWhiteSpace(note) ? null : note);
        return true;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
