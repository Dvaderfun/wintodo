using System.Net;

namespace Wintodo.Import.MicrosoftTodo;

/// <summary>Localised, self-contained browser response to an OAuth redirect.</summary>
public static class SignInResponsePage
{
    public static string Build(bool failed, string title, string completeText, string failedText)
        => "<!doctype html><html><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<title>" + WebUtility.HtmlEncode(title) + "</title>"
            + "<style>:root{color-scheme:light dark}body{font-family:system-ui,sans-serif;"
            + "margin:48px;background:#fff;color:#222}"
            + "@media(prefers-color-scheme:dark){body{background:#202020;color:#eee}}</style>"
            + "</head><body><p>" + WebUtility.HtmlEncode(failed ? failedText : completeText)
            + "</p></body></html>";
}
