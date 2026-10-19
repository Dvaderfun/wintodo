using Wintodo.Core.Import;

namespace Wintodo.Import.MicrosoftTodo;

public enum MicrosoftTodoReadPhase
{
    Account,
    Lists,
    Tasks,
    Attachments,
}

public readonly record struct MicrosoftTodoProgress(
    MicrosoftTodoReadPhase Phase,
    int Completed,
    int Total,
    string? ListName = null);

/// <summary>Everything read from one Microsoft To Do account, before mapping.</summary>
public sealed class MicrosoftTodoExport
{
    public GraphUser User { get; init; } = new();

    public List<MicrosoftTodoListData> Lists { get; } = [];
}

public sealed class MicrosoftTodoListData
{
    public GraphTodoList List { get; init; } = new();

    public List<GraphTodoTask> Tasks { get; init; } = [];
}

public sealed class MicrosoftTodoReader(GraphTodoClient client)
{
    private readonly GraphTodoClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<MicrosoftTodoExport> ReadAsync(
        IProgress<MicrosoftTodoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Account, 0, 1));
        GraphUser user = await _client.GetMeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            throw new GraphRequestException(System.Net.HttpStatusCode.OK, "missingUserId", "Graph did not return an account id.");
        }

        progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Lists, 0, 1));
        List<GraphTodoList> lists = await _client.GetListsAsync(cancellationToken).ConfigureAwait(false);
        var export = new MicrosoftTodoExport { User = user };
        for (int index = 0; index < lists.Count; index++)
        {
            GraphTodoList list = lists[index];
            progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Tasks, index, lists.Count, list.DisplayName));
            List<GraphTodoTask> tasks = await _client.GetTasksAsync(list.Id, cancellationToken).ConfigureAwait(false);
            foreach (GraphTodoTask task in tasks.Where(task => task.HasAttachments))
            {
                task.Attachments.AddRange(
                    await _client.GetAttachmentsAsync(list.Id, task.Id, cancellationToken).ConfigureAwait(false));
            }

            export.Lists.Add(new MicrosoftTodoListData { List = list, Tasks = tasks });
        }

        progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Tasks, lists.Count, lists.Count));
        return export;
    }

    /// <summary>
    /// Downloads every attachment in <paramref name="snapshot"/> into
    /// <paramref name="stagingDirectory"/> and records where it was saved.
    /// </summary>
    public async Task DownloadAttachmentsAsync(
        ImportSnapshot snapshot,
        string stagingDirectory,
        IProgress<MicrosoftTodoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        List<ImportAttachment> attachments = snapshot.Lists
            .SelectMany(list => list.Tasks)
            .SelectMany(task => task.Attachments)
            .Where(attachment => attachment.SourcePath is not null)
            .ToList();
        Directory.CreateDirectory(stagingDirectory);
        for (int index = 0; index < attachments.Count; index++)
        {
            progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Attachments, index, attachments.Count));
            ImportAttachment attachment = attachments[index];
            string[] parts = attachment.SourcePath!.Split('\n');
            byte[] content = await _client.GetAttachmentContentAsync(parts[0], parts[1], parts[2], cancellationToken)
                .ConfigureAwait(false);
            string path = Path.Combine(stagingDirectory, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await File.WriteAllBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
            attachment.StagedPath = path;
            attachment.SizeBytes = content.LongLength;
        }

        progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Attachments, attachments.Count, attachments.Count));
    }
}
