using System.Globalization;

namespace Wintodo.Core.Presentation;

public static class DisplayCultureResolver
{
    public static CultureInfo Resolve(string? languageTag, CultureInfo fallback)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageTag);
            }
            catch (CultureNotFoundException)
            {
                // An absent or invalid translation tag must not prevent startup.
            }
        }

        return fallback;
    }
}
