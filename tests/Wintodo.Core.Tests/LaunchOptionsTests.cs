using Wintodo.Core.Services;
using Wintodo.Services;

namespace Wintodo.Core.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Value_UsesTheFirstExactOptionAndPreservesSpaces()
    {
        string[] arguments = ["app", "--database-path", "scratch database/tasks.db", "--database-path", "other.db"];

        Assert.Equal("scratch database/tasks.db", LaunchOptions.GetValue(arguments, "--database-path"));
    }

    [Theory]
    [InlineData("--DATABASE-PATH")]
    [InlineData("--database-path=tasks.db")]
    [InlineData("--unrelated")]
    public void Value_IgnoresOtherNamesAndInlineForms(string suppliedOption)
    {
        Assert.Null(LaunchOptions.GetValue(["app", suppliedOption, "tasks.db"], "--database-path"));
    }

    [Fact]
    public void Value_IgnoresTheExecutableArgumentAndAllowsAnOptionLookingValue()
    {
        Assert.Null(LaunchOptions.GetValue(["--database-path", "tasks.db"], "--database-path"));
        Assert.Equal("--clock-file", LaunchOptions.GetValue(["app", "--database-path", "--clock-file"], "--database-path"));
    }

    [Fact]
    public void Value_MissingFollowingArgumentKeepsTheExistingFailure()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => LaunchOptions.GetValue(["app", "--database-path"], "--database-path"));

        Assert.Equal("option", exception.ParamName);
        Assert.StartsWith("--database-path requires a value.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flag_UsesExactNamesAfterTheExecutableArgument()
    {
        Assert.True(LaunchOptions.HasFlag(["app", "--show-onboarding"], "--show-onboarding"));
        Assert.False(LaunchOptions.HasFlag(["app", "--SHOW-ONBOARDING"], "--show-onboarding"));
        Assert.False(LaunchOptions.HasFlag(["--show-onboarding"], "--show-onboarding"));
    }

    [Fact]
    public void Database_CommandLineWinsWithoutReadingLowerPriorityOrDefault()
    {
        string input = Path.Combine("scratch database", "tasks.db");

        string actual = LaunchOptions.ResolveDatabasePath(input,
            () => throw new InvalidOperationException("Lower-priority lookup must be lazy."),
            () => throw new InvalidOperationException("Default storage lookup must be lazy."));

        Assert.Equal(Path.GetFullPath(input), actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Database_AbsentCommandLineUsesTheEnvironmentOrEligibleNotificationFallback(string? commandLinePath)
    {
        string environmentPath = Path.Combine("environment database", "tasks.db");
        int lookupCount = 0;

        string actual = LaunchOptions.ResolveDatabasePath(commandLinePath,
            () => { lookupCount++; return environmentPath; },
            () => throw new InvalidOperationException("Default storage lookup must be lazy."));

        Assert.Equal(Path.GetFullPath(environmentPath), actual);
        Assert.Equal(1, lookupCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Database_BlankLowerPriorityInputUsesTheUnmodifiedPackageDefault(string? overridePath)
    {
        int lookupCount = 0;
        string defaultPath = Path.Combine("package LocalState", "wintodo.db");

        string actual = LaunchOptions.ResolveDatabasePath(null,
            () => overridePath,
            () => { lookupCount++; return defaultPath; });

        Assert.Equal(defaultPath, actual);
        Assert.Equal(1, lookupCount);
    }

    [Fact]
    public void Database_InvalidSelectedPathFailsWithoutTryingOtherLocations()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.ResolveDatabasePath("invalid\0path",
            () => throw new InvalidOperationException("Must not hide the selected path failure."),
            () => throw new InvalidOperationException("Must not hide the selected path failure.")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Onboarding_WithoutCommandLineDatabaseIsAllowedWithoutQueryingOptIn(string? commandLinePath)
    {
        Assert.True(LaunchOptions.AllowsOnboarding(commandLinePath,
            () => throw new InvalidOperationException("Opt-in lookup must be lazy.")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Onboarding_CommandLineDatabaseNeedsExplicitOptIn(bool explicitOptIn)
    {
        Assert.Equal(explicitOptIn, LaunchOptions.AllowsOnboarding("scratch database/tasks.db", () => explicitOptIn));
    }

    [Fact]
    public void Onboarding_EnvironmentSelectedDatabaseRetainsOrdinaryChecks()
    {
        string selected = LaunchOptions.ResolveDatabasePath(null, () => "environment.db",
            () => throw new InvalidOperationException("Environment database should win."));

        Assert.Equal(Path.GetFullPath("environment.db"), selected);
        Assert.True(LaunchOptions.AllowsOnboarding(null,
            () => throw new InvalidOperationException("Environment alone must not require opt-in.")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Clock_WithoutFileUsesRealTimeAndTheLocalTimezone(string? clockFile)
    {
        IClock actual = LaunchOptions.CreateClock(clockFile);
        var reference = new SystemClock();
        DateTimeOffset before = reference.UtcNow;
        DateTimeOffset observed = actual.UtcNow;
        DateTimeOffset after = reference.UtcNow;

        Assert.IsType<SystemClock>(actual);
        Assert.InRange(observed, before, after);
        Assert.Equal(TimeZoneInfo.Local, actual.LocalTimeZone);
    }

    [Fact]
    public void Clock_ReadsTheSelectedFileEveryTimeAndNormalizesUtc()
    {
        using var fixture = new ClockFile();
        File.WriteAllText(fixture.Path, " 2026-11-09T14:00:00+02:00 \n");
        IClock actual = LaunchOptions.CreateClock(fixture.Path);

        Assert.IsType<FileBackedClock>(actual);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), actual.UtcNow);
        Assert.Equal(TimeZoneInfo.Local, actual.LocalTimeZone);

        File.WriteAllText(fixture.Path, "2026-11-10T08:00:00");
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), actual.UtcNow);
    }

    [Fact]
    public void Clock_InvalidOrMissingFileFailsOnReadWithoutUsingRealTime()
    {
        using var fixture = new ClockFile();
        IClock actual = LaunchOptions.CreateClock(fixture.Path);

        Assert.Throws<FileNotFoundException>(() => actual.UtcNow);
        File.WriteAllText(fixture.Path, "invalid clock text");
        Assert.Throws<FormatException>(() => actual.UtcNow);
    }

    private sealed class ClockFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            AppContext.BaseDirectory, "launch-options-tests", Guid.NewGuid().ToString("N"));

        internal ClockFile()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "clock file.txt");
        }

        internal string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
            Directory.Delete(_directory);
        }
    }
}
