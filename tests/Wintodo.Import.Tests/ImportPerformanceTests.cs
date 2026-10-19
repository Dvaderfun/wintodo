using System.Diagnostics;
using System.Text.Json;
using Wintodo.Core.Import;
using Wintodo.Import.MicrosoftTodo;

namespace Wintodo.Import.Tests;

public sealed class ImportPerformanceTests
{
    [Fact]
    public async Task MeasureLatencyFixtureSerialAndBoundedCandidate()
    {
        if (Environment.GetEnvironmentVariable("WINTODO_IMPORT_PERF") != "1") return;
        CancellationToken token = TestContext.Current.CancellationToken;
        await using TempStore fixture = await TempStore.CreateAsync();
        string root = Path.Combine(fixture.Directory, "graph");
        var lists = new GraphCollection<GraphTodoList>();
        for (int i = 0; i < 20; i++)
        {
            string id = "L" + i;
            lists.Value.Add(new GraphTodoList { Id = id, DisplayName = "Fixture list " + i });
            var tasks = new GraphCollection<GraphTodoTask>();
            for (int j = 0; j < 50; j++) tasks.Value.Add(new GraphTodoTask
            {
                Id = "T" + i + "-" + j, Title = "Fixture task " + i + "-" + j,
                CreatedDateTime = fixture.Clock.UtcNow, LastModifiedDateTime = fixture.Clock.UtcNow,
                ChecklistItems = [], LinkedResources = [],
            });
            string path = Path.Combine(root, "me", "todo", "lists", id, "tasks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tasks, GraphJsonContext.Default.GraphCollectionGraphTodoTask), token);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "me.json"), JsonSerializer.Serialize(new GraphUser { Id = "perf-account" }, GraphJsonContext.Default.GraphUser), token);
        await File.WriteAllTextAsync(Path.Combine(root, "me", "todo", "lists", "delta.json"), JsonSerializer.Serialize(lists, GraphJsonContext.Default.GraphCollectionGraphTodoList), token);
        var results = new List<string>();
        foreach (bool parallel in new[] { false, true })
        {
            await using TempStore store = await TempStore.CreateAsync();
            var client = new GraphTodoClient(new HttpClient(new FixtureGraphHandler(root, latency: TimeSpan.FromMilliseconds(50))), "fixture");
            Stopwatch timer = Stopwatch.StartNew();
            MicrosoftTodoExport export;
            if (!parallel) export = await new MicrosoftTodoReader(client).ReadAsync(cancellationToken: token);
            else
            {
                GraphUser user = await client.GetMeAsync(token);
                List<GraphTodoList> sourceLists = await client.GetListsAsync(token);
                var data = new MicrosoftTodoListData[sourceLists.Count];
                await Parallel.ForEachAsync(Enumerable.Range(0, sourceLists.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (index, ct) =>
                        data[index] = new MicrosoftTodoListData { List = sourceLists[index], Tasks = await client.GetTasksAsync(sourceLists[index].Id, ct) });
                export = new MicrosoftTodoExport { User = user };
                export.Lists.AddRange(data);
            }
            double read = timer.Elapsed.TotalMilliseconds;
            ImportSnapshot snapshot = GraphTodoMapper.Map(export, TestSupport.Options(store.Clock.UtcNow));
            Stopwatch preview = Stopwatch.StartNew();
            await store.Store.PreviewImportAsync(snapshot, new(), token);
            double previewMs = preview.Elapsed.TotalMilliseconds;
            await store.Store.ApplyImportAsync(snapshot, new(), token);
            results.Add(FormattableString.Invariant($"| {(parallel ? "production bounded (4 lists)" : "production serial (1 list)")} | {read:F1} ms | {previewMs:F1} ms | {timer.Elapsed.TotalMilliseconds:F1} ms |"));
            Assert.Equal(lists.Value.Select(list => list.Id), export.Lists.Select(list => list.List.Id));
        }
        string output = Environment.GetEnvironmentVariable("WINTODO_IMPORT_PERF_OUTPUT")!;
        await File.WriteAllLinesAsync(output, results, token);
    }
}
