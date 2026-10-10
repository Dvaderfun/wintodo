using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Core.Queries;

namespace Wintodo.Data.Tests;

[Collection("Performance")]
public sealed class SmartListPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public SmartListPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TenThousandTasks_EachSmartListQueryCompletesWithinFiveMilliseconds()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Benchmark", cancellationToken: TestContext.Current.CancellationToken);
        await SeedTasksAsync(fixture, list.Id, count: 10_000);

        var queries = new Dictionary<string, ListQuery>
        {
            ["My Day"] = Smart(ListQueryKind.MyDay, SortMode.Manual),
            ["Important"] = Smart(ListQueryKind.Important, SortMode.Importance),
            ["Planned"] = Smart(ListQueryKind.Planned, SortMode.DueDate),
            ["All"] = Smart(ListQueryKind.All, SortMode.CreationDate),
            ["Completed"] = Smart(ListQueryKind.Completed, SortMode.CreationDate),
        };

        foreach ((string name, ListQuery query) in queries)
        {
            await fixture.Store.GetTasksAsync(query, TestContext.Current.CancellationToken);
            double bestMilliseconds = double.MaxValue;
            for (int attempt = 0; attempt < 7; attempt++)
            {
                long start = Stopwatch.GetTimestamp();
                IReadOnlyList<TodoTask> result = await fixture.Store.GetTasksAsync(query, TestContext.Current.CancellationToken);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                bestMilliseconds = Math.Min(bestMilliseconds, elapsed);
                Assert.NotEmpty(result);
            }

            _output.WriteLine($"{name}: {bestMilliseconds:F3} ms");
            Assert.True(
                bestMilliseconds <= 5,
                $"{name} best warm query took {bestMilliseconds:F3} ms; budget is 5 ms.");
        }
    }

    [Fact]
    public async Task TenThousandTasks_FullTextSearchCompletesWithinSixtyMilliseconds()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        TodoList list = await fixture.Store.CreateListAsync("Search benchmark", cancellationToken: TestContext.Current.CancellationToken);
        await SeedTasksAsync(fixture, list.Id, count: 10_000);

        await fixture.Store.SearchAsync("Task 09999", cancellationToken: TestContext.Current.CancellationToken);
        double worstMilliseconds = 0;
        for (int attempt = 0; attempt < 7; attempt++)
        {
            long start = Stopwatch.GetTimestamp();
            IReadOnlyList<TodoTask> result =
                await fixture.Store.SearchAsync("Task 09999", cancellationToken: TestContext.Current.CancellationToken);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            worstMilliseconds = Math.Max(worstMilliseconds, elapsed);
            Assert.Equal("Task 09999", Assert.Single(result).Summary);
        }

        _output.WriteLine($"FTS search worst warm query: {worstMilliseconds:F3} ms");
        Assert.True(
            worstMilliseconds <= 60,
            $"FTS search worst warm query took {worstMilliseconds:F3} ms; budget is 60 ms.");
    }

    private static ListQuery Smart(ListQueryKind kind, SortMode sortMode)
    {
        return new ListQuery
        {
            Kind = kind,
            Limit = 100,
            SortMode = sortMode,
            IncludeCompleted = kind is ListQueryKind.All or ListQueryKind.Completed,
        };
    }

    private static async Task SeedTasksAsync(StoreFixture fixture, long listId, int count)
    {
        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tasks(
                uid, list_id, summary, status, priority, due_utc, completed_utc,
                my_day_date, sort_order, created_utc, modified_utc)
            VALUES(
                @uid, @listId, @summary, @status, @priority, @dueUtc,
                @completedUtc, @myDayDate, @sortOrder, @createdUtc, @modifiedUtc);
            """;
        SqliteParameter uid = command.Parameters.Add("@uid", SqliteType.Text);
        command.Parameters.AddWithValue("@listId", listId);
        SqliteParameter summary = command.Parameters.Add("@summary", SqliteType.Text);
        SqliteParameter status = command.Parameters.Add("@status", SqliteType.Integer);
        SqliteParameter priority = command.Parameters.Add("@priority", SqliteType.Integer);
        SqliteParameter due = command.Parameters.Add("@dueUtc", SqliteType.Text);
        SqliteParameter completed = command.Parameters.Add("@completedUtc", SqliteType.Text);
        SqliteParameter myDay = command.Parameters.Add("@myDayDate", SqliteType.Text);
        SqliteParameter sortOrder = command.Parameters.Add("@sortOrder", SqliteType.Real);
        SqliteParameter created = command.Parameters.Add("@createdUtc", SqliteType.Text);
        SqliteParameter modified = command.Parameters.Add("@modifiedUtc", SqliteType.Text);

        for (int index = 0; index < count; index++)
        {
            uid.Value = Guid.NewGuid().ToString("D");
            summary.Value = $"Task {index:D5}";
            status.Value = index % 5 == 0
                ? (int)Wintodo.Core.Models.TaskStatus.Completed
                : (int)Wintodo.Core.Models.TaskStatus.NeedsAction;
            priority.Value = index % 10;
            due.Value = index % 2 == 0
                ? new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)
                    .AddSeconds(index)
                    .UtcDateTime
                    .ToString(
                        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                        System.Globalization.CultureInfo.InvariantCulture)
                : DBNull.Value;
            completed.Value = index % 5 == 0
                ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    .AddSeconds(index)
                    .UtcDateTime
                    .ToString(
                        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                        System.Globalization.CultureInfo.InvariantCulture)
                : DBNull.Value;
            myDay.Value = index % 3 == 0 ? "2026-01-05" : DBNull.Value;
            sortOrder.Value = index;
            string timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddSeconds(index)
                .UtcDateTime
                .ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    System.Globalization.CultureInfo.InvariantCulture);
            created.Value = timestamp;
            modified.Value = timestamp;
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}

[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceCollection;
