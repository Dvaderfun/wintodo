namespace Wintodo.Core.Services;

[Flags]
public enum TaskDetailFields
{
    None = 0,
    Summary = 1,
    Description = 2,
    Due = 4,
    Reminder = 8,
    Repeat = 16,
    Importance = 32,
    RemoveAttachment = 64,
}
