using Wintodo.Services;

namespace Wintodo.ViewModels;

public sealed partial class ShellViewModel
{
    internal string GetUniqueFileName(string displayName, ISet<string> usedNames)
    {
        string stem = SanitizeFileName(displayName);
        string candidate = stem + ".ics";
        int suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = string.Format(
                DisplayCulture.Current,
                _resources.GetString("Export_DuplicateFileNameFormat"),
                stem,
                suffix++);
        }

        return candidate;
    }

    internal string SanitizeFileName(string value)
    {
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        string sanitized = new(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        sanitized = sanitized.Trim().TrimEnd('.');
        if (sanitized.Length == 0)
        {
            sanitized = _resources.GetString("Export_FallbackFileName");
        }

        if (sanitized.Length <= 96)
        {
            return sanitized;
        }

        int length = char.IsHighSurrogate(sanitized[95]) ? 95 : 96;
        return sanitized[..length];
    }
}
