using Wintodo.Core.Models;

namespace Wintodo.Core.Ical;

public sealed class IcalCalendar
{
    public string? Name { get; set; }

    public ListColor? Color { get; set; }

    public List<TodoTask> Tasks { get; } = [];
}
