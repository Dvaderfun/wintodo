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
    string? ListName = null,
    long CompletedBytes = 0,
    long TotalBytes = 0);

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

public sealed class MicrosoftTodoReader(GraphTodoClient client, int listConcurrency = 4)
{
    private const long MaximumImportAttachmentBytes = 256L * 1024 * 1024;
    private readonly GraphTodoClient _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Verifies identity before reusing an export after a fresh sign-in.</summary>
    public static async Task<bool> CanResumeAsync(GraphTodoClient client, MicrosoftTodoExport export,
        CancellationToken cancellationToken = default)
    {
        GraphUser user = await client.GetMeAsync(cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(user.Id) && string.Equals(user.Id, export.User.Id, StringComparison.Ordinal);
    }

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
        var results = new MicrosoftTodoListData[lists.Count];
        int completed = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, lists.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(listConcurrency, 1, 4), CancellationToken = cancellationToken },
            async (index, token) =>
            {
                GraphTodoList list = lists[index];
                List<GraphTodoTask> tasks = await _client.GetTasksAsync(list.Id, token).ConfigureAwait(false);
                foreach (GraphTodoTask task in tasks.Where(task => task.HasAttachments))
                    task.Attachments.AddRange(await _client.GetAttachmentsAsync(list.Id, task.Id, token).ConfigureAwait(false));
                results[index] = new MicrosoftTodoListData { List = list, Tasks = tasks };
                progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Tasks,
                    Interlocked.Increment(ref completed), lists.Count));
            }).ConfigureAwait(false);

        export.Lists.AddRange(results);

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
        IReadOnlySet<string> pendingAttachmentIds,
        IProgress<MicrosoftTodoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentNullException.ThrowIfNull(pendingAttachmentIds);
        List<ImportAttachment> attachments = snapshot.Lists
            .SelectMany(list => list.Tasks)
            .SelectMany(task => task.Attachments)
            .Where(attachment => attachment.SourcePath is not null && pendingAttachmentIds.Contains(attachment.ExternalId))
            .ToList();
        long totalBytes = 0;
        foreach (ImportAttachment attachment in attachments)
        {
            long declaredBytes = Math.Max(0, attachment.SizeBytes);
            if (declaredBytes > GraphTodoClient.MaximumAttachmentBytes
                || declaredBytes > MaximumImportAttachmentBytes - totalBytes)
                throw new InvalidDataException("The import attachments exceed the byte budget.");
            totalBytes += declaredBytes;
        }
        long completedBytes = 0;
        Directory.CreateDirectory(stagingDirectory);
        for (int index = 0; index < attachments.Count; index++)
        {
            progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Attachments, index, attachments.Count));
            ImportAttachment attachment = attachments[index];
            string[] parts = attachment.SourcePath!.Split('\n');
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("The attachment reference is malformed.");
            string path = Path.Combine(stagingDirectory, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            bool created = false;
            long size;
            try
            {
                await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    created = true;
                    size = await _client.CopyAttachmentContentAsync(parts[0], parts[1], parts[2], file,
                        bytes => progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Attachments,
                            index, attachments.Count, CompletedBytes: completedBytes + bytes, TotalBytes: totalBytes)),
                        cancellationToken, MaximumImportAttachmentBytes - completedBytes).ConfigureAwait(false);
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                DownloadedAttachmentProvenance.MarkInternet(path);
            }
            catch
            {
                // Only this attempt's newly created file is removable. Existing
                // numbered files and earlier complete downloads are preserved.
                if (created) File.Delete(path);
                throw;
            }
            attachment.StagedPath = path;
            attachment.SizeBytes = size;
            completedBytes += size;
        }

        progress?.Report(new MicrosoftTodoProgress(MicrosoftTodoReadPhase.Attachments, attachments.Count,
            attachments.Count, CompletedBytes: completedBytes, TotalBytes: totalBytes));
    }
}
