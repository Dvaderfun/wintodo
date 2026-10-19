using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wintodo.Import.MicrosoftTodo;

// Microsoft Graph v1.0 To Do resources. Only read, never written back.
// https://learn.microsoft.com/graph/api/resources/todo-overview

public sealed class GraphCollection<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

public sealed class GraphUser
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Mail { get; set; }

    public string? UserPrincipalName { get; set; }
}

public sealed class GraphTodoList
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public bool IsOwner { get; set; } = true;

    public bool IsShared { get; set; }

    public string? WellknownListName { get; set; }

    /// <summary>Set on delta entries for lists that no longer exist.</summary>
    [JsonPropertyName("@removed")]
    public GraphRemoved? Removed { get; set; }
}

public sealed class GraphRemoved
{
    public string? Reason { get; set; }
}

public sealed class GraphTodoTask
{
    public string Id { get; set; } = string.Empty;

    public string? Title { get; set; }

    public GraphItemBody? Body { get; set; }

    public string? Importance { get; set; }

    public string? Status { get; set; }

    public bool IsReminderOn { get; set; }

    public DateTimeOffset? CreatedDateTime { get; set; }

    public DateTimeOffset? LastModifiedDateTime { get; set; }

    public DateTimeOffset? BodyLastModifiedDateTime { get; set; }

    public GraphDateTimeTimeZone? CompletedDateTime { get; set; }

    public GraphDateTimeTimeZone? DueDateTime { get; set; }

    public GraphDateTimeTimeZone? StartDateTime { get; set; }

    public GraphDateTimeTimeZone? ReminderDateTime { get; set; }

    public GraphPatternedRecurrence? Recurrence { get; set; }

    public List<string>? Categories { get; set; }

    public bool HasAttachments { get; set; }

    public List<GraphChecklistItem>? ChecklistItems { get; set; }

    public List<GraphLinkedResource>? LinkedResources { get; set; }

    /// <summary>Properties this model doesn't know yet. Kept so nothing is dropped.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    [JsonIgnore]
    public List<GraphAttachment> Attachments { get; } = [];
}

public sealed class GraphItemBody
{
    public string? Content { get; set; }

    public string? ContentType { get; set; }
}

public sealed class GraphDateTimeTimeZone
{
    public string? DateTime { get; set; }

    public string? TimeZone { get; set; }
}

public sealed class GraphPatternedRecurrence
{
    public GraphRecurrencePattern? Pattern { get; set; }

    public GraphRecurrenceRange? Range { get; set; }
}

public sealed class GraphRecurrencePattern
{
    public string? Type { get; set; }

    public int Interval { get; set; } = 1;

    public int Month { get; set; }

    public int DayOfMonth { get; set; }

    public List<string>? DaysOfWeek { get; set; }

    public string? FirstDayOfWeek { get; set; }

    public string? Index { get; set; }
}

public sealed class GraphRecurrenceRange
{
    public string? Type { get; set; }

    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public string? RecurrenceTimeZone { get; set; }

    public int NumberOfOccurrences { get; set; }
}

public sealed class GraphChecklistItem
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public bool IsChecked { get; set; }

    public DateTimeOffset? CreatedDateTime { get; set; }

    public DateTimeOffset? CheckedDateTime { get; set; }
}

public sealed class GraphLinkedResource
{
    public string? Id { get; set; }

    public string? WebUrl { get; set; }

    public string? ApplicationName { get; set; }

    public string? DisplayName { get; set; }

    public string? ExternalId { get; set; }
}

public sealed class GraphAttachment
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ContentType { get; set; }

    public long Size { get; set; }

    public DateTimeOffset? LastModifiedDateTime { get; set; }

    public string? ContentBytes { get; set; }
}

public sealed class GraphErrorResponse
{
    public GraphError? Error { get; set; }
}

public sealed class GraphError
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphUser))]
[JsonSerializable(typeof(GraphCollection<GraphTodoList>))]
[JsonSerializable(typeof(GraphCollection<GraphTodoTask>))]
[JsonSerializable(typeof(GraphCollection<GraphChecklistItem>))]
[JsonSerializable(typeof(GraphCollection<GraphLinkedResource>))]
[JsonSerializable(typeof(GraphCollection<GraphAttachment>))]
[JsonSerializable(typeof(GraphAttachment))]
[JsonSerializable(typeof(GraphPatternedRecurrence))]
[JsonSerializable(typeof(GraphLinkedResource))]
[JsonSerializable(typeof(GraphErrorResponse))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class GraphJsonContext : JsonSerializerContext
{
}
