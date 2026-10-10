namespace Wintodo.Core.Services;

/// <summary>A field-level action applied atomically to a set of task UIDs.</summary>
public enum TaskBatchAction
{
    Complete,
    Uncomplete,
    MarkImportant,
    RemoveImportance,
    AddToMyDay,
    RemoveFromMyDay,
    SetDueDate,
    Move,
    Delete,
    Restore,
}
