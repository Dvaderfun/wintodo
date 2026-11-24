using Wintodo.Core.Services;

namespace Wintodo.Services;

/// <summary>Shared, platform-free decisions for the existing local launch controls.</summary>
internal static class LaunchOptions
{
    internal const string NewTaskArgument = "wintodo:new-task";
    internal const string OpenListArgumentPrefix = "wintodo:list:";

    internal static string? GetJumpListArgument(string[] arguments)
    {
        // Each published JumpListItem carries a single token. An option value in a
        // development launch (or a token-looking executable name) is not a jump-list action.
        if (arguments.Length != 2) return null;
        string token = arguments[1];
        if (token == NewTaskArgument) return token;
        return token.StartsWith(OpenListArgumentPrefix, StringComparison.Ordinal)
            && token.Length == OpenListArgumentPrefix.Length + 36
            && Guid.TryParseExact(token.AsSpan(OpenListArgumentPrefix.Length), "D", out _) ? token : null;
    }

    internal static string? GetValue(string[] arguments, string option)
    {
        for (int index = 1; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= arguments.Length)
            {
                throw new ArgumentException(
                    $"{option} requires a value.",
                    nameof(option));
            }

            return arguments[index + 1];
        }

        return null;
    }

    internal static bool HasFlag(string[] arguments, string flag)
    {
        return arguments.Skip(1).Contains(flag, StringComparer.Ordinal);
    }

    internal static string ResolveDatabasePath(
        string? commandLinePath,
        Func<string?> getLowerPriorityOverride,
        Func<string> getDefaultPath)
    {
        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            return Path.GetFullPath(commandLinePath);
        }

        // The caller retains platform-specific Debug notification and environment
        // precedence. Neither that lookup nor LocalFolder is needed for a CLI path.
        string? overridePath = getLowerPriorityOverride();
        return string.IsNullOrWhiteSpace(overridePath)
            ? getDefaultPath()
            : Path.GetFullPath(overridePath);
    }

    internal static bool AllowsOnboarding(
        string? commandLineDatabasePath,
        Func<bool> hasExplicitOptIn)
    {
        return string.IsNullOrWhiteSpace(commandLineDatabasePath) || hasExplicitOptIn();
    }

    internal static IClock CreateClock(string? clockFile)
    {
        return string.IsNullOrWhiteSpace(clockFile)
            ? new SystemClock()
            : new FileBackedClock(clockFile);
    }
}
