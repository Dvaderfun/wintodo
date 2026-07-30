using Microsoft.Data.Sqlite;
using Wintodo.Core.Models;
using Wintodo.Data;

namespace Wintodo.Data.Tests;

public sealed class M4PersistenceGateTests
{
    [Fact]
    public async Task DetailFieldsStepsAndBiweeklyCompletion_SurviveRestart()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Wintodo.Tests",
            Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "wintodo.db");
        var clock = new FakeClock();
        string taskUid;
        string editedStepUid;
        string completedStepUid;
        DateTimeOffset firstDue = new(2026, 1, 6, 9, 0, 0, TimeSpan.Zero);

        try
        {
            await using (var store = new SqliteTaskStore(databasePath, clock))
            {
                await store.InitializeAsync();
                TodoList list = await store.CreateListAsync("M4 gate");
                TodoTask task = await store.CreateTaskAsync(new TodoTask
                {
                    ListId = list.Id,
                    Summary = "Ship the detail pane",
                    Description = "Round-trip the complete detail surface.",
                    Priority = (int)TaskPriority.High,
                    Due = firstDue,
                    RRule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU",
                    MyDayDate = clock.Today,
                });
                taskUid = task.Uid;
                task.Alarms.Add(new TaskAlarm
                {
                    Action = AlarmAction.Display,
                    Trigger = firstDue.AddHours(-1),
                    Description = task.Summary,
                });
                task.Attachments.Add(new TaskAttachment
                {
                    FileName = "gate.txt",
                    RelativePath = Path.Combine("attachments", task.Uid, "gate.txt"),
                    SizeBytes = 128,
                    MimeType = "text/plain",
                    Added = clock.UtcNow,
                });
                await store.UpdateTaskAsync(task);

                TodoTask editedStep = await store.AddStepAsync(task.Uid, "Draft");
                editedStep.Summary = "Draft release notes";
                await store.UpdateTaskAsync(editedStep);
                editedStepUid = editedStep.Uid;

                TodoTask completedStep = await store.AddStepAsync(task.Uid, "Run tests");
                await store.CompleteTaskAsync(completedStep.Uid);
                await store.ReorderStepAsync(completedStep.Uid, 0.5);
                completedStepUid = completedStep.Uid;

                TodoTask deletedStep = await store.AddStepAsync(task.Uid, "Temporary");
                await store.DeleteTaskAsync(deletedStep.Uid);
            }

            await using (var reopened = new SqliteTaskStore(databasePath, clock))
            {
                await reopened.InitializeAsync();
                TodoTask stored = Assert.IsType<TodoTask>(
                    await reopened.GetTaskAsync(taskUid));
                Assert.Equal("Ship the detail pane", stored.Summary);
                Assert.Equal("Round-trip the complete detail surface.", stored.Description);
                Assert.Equal((int)TaskPriority.High, stored.Priority);
                Assert.Equal(firstDue, stored.Due);
                Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=TU", stored.RRule);
                Assert.Equal(clock.Today, stored.MyDayDate);
                Assert.Equal(firstDue.AddHours(-1), Assert.Single(stored.Alarms).Trigger);
                Assert.Equal("gate.txt", Assert.Single(stored.Attachments).FileName);

                IReadOnlyList<TodoTask> steps = await reopened.GetChildrenAsync(taskUid);
                Assert.Equal([completedStepUid, editedStepUid], steps.Select(step => step.Uid));
                Assert.Equal(
                    Wintodo.Core.Models.TaskStatus.Completed,
                    steps[0].Status);
                Assert.Equal("Draft release notes", steps[1].Summary);

                await reopened.CompleteTaskAsync(taskUid);
                TodoTask advanced = Assert.IsType<TodoTask>(
                    await reopened.GetTaskAsync(taskUid));
                Assert.Equal(firstDue.AddDays(14), advanced.Due);
                Assert.Equal(
                    Wintodo.Core.Models.TaskStatus.NeedsAction,
                    advanced.Status);
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM completion_history WHERE task_uid = @uid;";
            command.Parameters.AddWithValue("@uid", taskUid);
            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
