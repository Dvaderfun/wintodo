using Wintodo.Core.Models;

namespace Wintodo.Core.Services;

[Flags]
public enum TaskUndoFields
{
    None = 0,
    Deleted = 1,
    List = 2,
    Due = 4,
    Reminder = 8,
    Repeat = 16,
    Completion = 32,
    Importance = 64,
    MyDay = 128,
}

/// <summary>Only the indicated fields are restored; current sync identity and other edits survive.</summary>
public sealed record TaskUndoState(TodoTask Task, TaskUndoFields Fields, int CompletionCount);
