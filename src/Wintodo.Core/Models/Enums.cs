namespace Wintodo.Core.Models;

public enum TaskStatus
{
    NeedsAction = 0,
    InProcess = 1,
    Completed = 2,
    Cancelled = 3,
}

public enum TaskPriority
{
    Undefined = 0,
    High = 1,
    Medium = 5,
    Low = 9,
}

public enum Classification
{
    Public = 0,
    Private = 1,
    Confidential = 2,
}

public enum ListColor
{
    Blue = 0,
    Red,
    Orange,
    Yellow,
    Green,
    Teal,
    Lime,
    Purple,
    Pink,
    Olive,
    Graphite,
    Navy,
}

public enum SortMode
{
    Manual = 0,
    Importance,
    DueDate,
    Alphabetical,
    CreationDate,
}

public enum AlarmAction
{
    Display = 0,
    Audio = 1,
    Email = 2,
}

public enum AlarmRelation
{
    Start = 0,
    End = 1,
}

public enum ChangeOperation
{
    Create = 0,
    Update = 1,
    Delete = 2,
}
