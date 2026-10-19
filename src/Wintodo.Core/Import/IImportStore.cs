namespace Wintodo.Core.Import;

public interface IImportStore
{
    /// <summary>Runs the whole import inside a transaction that is rolled back, and returns its counts.</summary>
    Task<ImportOutcome> PreviewImportAsync(
        ImportSnapshot snapshot,
        ImportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the snapshot in one transaction. Items imported before are matched
    /// through their links and are never duplicated.
    /// </summary>
    Task<ImportOutcome> ApplyImportAsync(
        ImportSnapshot snapshot,
        ImportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the lists, tasks and steps created by one run and forgets their links.</summary>
    Task<int> RemoveImportRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportSourceInfo>> GetImportSourcesAsync(
        string provider,
        CancellationToken cancellationToken = default);
}
