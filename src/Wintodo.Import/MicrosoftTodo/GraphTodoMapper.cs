using System.Globalization;
using System.Text.Json;
using Wintodo.Core.Ical;
using Wintodo.Core.Import;
using Wintodo.Core.Models;
using TaskStatus = Wintodo.Core.Models.TaskStatus;

namespace Wintodo.Import.MicrosoftTodo;

public sealed class MicrosoftTodoMapOptions
{
    /// <summary>Lists to import, by Graph id. Null imports every list.</summary>
    public IReadOnlySet<string>? SelectedListIds { get; init; }

    public bool IncludeCompleted { get; init; } = true;

    public bool IncludeAttachments { get; init; } = true;

    /// <summary>Title for a task whose To Do title is empty (localised by the caller).</summary>
    public string UntitledTaskTitle { get; init; } = "Untitled task";

    public TimeZoneInfo LocalTimeZone { get; init; } = TimeZoneInfo.Local;

    public DateTimeOffset Now { get; init; }
}

/// <summary>Maps Graph To Do data onto Wintodo models without losing source values.</summary>
public static class GraphTodoMapper
{
    public const string Provider = "mstodo";

    public const string IdProperty = "X-MSTODO-ID";
    public const string ListIdProperty = "X-MSTODO-LIST-ID";
    public const string StatusProperty = "X-MSTODO-STATUS";
    public const string RecurrenceProperty = "X-MSTODO-RECURRENCE";
    public const string LinkedResourceProperty = "X-MSTODO-LINKED-RESOURCE";
    public const string ExtraProperty = "X-MSTODO-EXTRA";
    public const string HtmlBodyProperty = "X-ALT-DESC";

    public static IReadOnlyList<string> ManagedProperties { get; } =
    [
        IdProperty,
        ListIdProperty,
        StatusProperty,
        RecurrenceProperty,
        LinkedResourceProperty,
        ExtraProperty,
        HtmlBodyProperty,
    ];

    public static ImportSnapshot Map(MicrosoftTodoExport export, MicrosoftTodoMapOptions options)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(options);
        GraphUser user = export.User;
        var snapshot = new ImportSnapshot
        {
            Provider = Provider,
            AccountKey = user.Id,
            AccountDisplayName = FirstNonEmpty(user.Mail, user.UserPrincipalName, user.DisplayName) ?? user.Id,
        };
        snapshot.ManagedPropertyNames.AddRange(ManagedProperties);

        foreach (MicrosoftTodoListData data in export.Lists)
        {
            if (options.SelectedListIds is not null && !options.SelectedListIds.Contains(data.List.Id))
            {
                continue;
            }

            snapshot.Lists.Add(MapList(snapshot, data, options));
        }

        return snapshot;
    }

    public static ImportListKind GetListKind(GraphTodoList list)
    {
        return list.WellknownListName?.Trim().ToLowerInvariant() switch
        {
            "defaultlist" => ImportListKind.Default,
            "flaggedemails" => ImportListKind.FlaggedEmail,
            _ => ImportListKind.User,
        };
    }

    /// <summary>
    /// Splits a leading emoji off a list name ("🛒 Groceries" → 🛒 + "Groceries").
    /// Microsoft To Do keeps a list's emoji icon at the start of its name.
    /// </summary>
    public static (string? Emoji, string Name) SplitListEmoji(string? displayName)
    {
        string name = displayName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return (null, name);
        }

        int length = StringInfo.GetNextTextElementLength(name);
        string first = name[..length];
        string rest = name[length..].Trim();
        return rest.Length > 0 && ListEmoji.TryNormalize(first, out string? emoji) && emoji is not null
            ? (emoji, rest)
            : (null, name);
    }

    private static ImportList MapList(ImportSnapshot snapshot, MicrosoftTodoListData data, MicrosoftTodoMapOptions options)
    {
        (string? emoji, string name) = SplitListEmoji(data.List.DisplayName);
        var list = new ImportList
        {
            ExternalId = data.List.Id,
            DisplayName = name.Length == 0 ? options.UntitledTaskTitle : name,
            IconEmoji = emoji,
            Kind = GetListKind(data.List),
            IsShared = data.List.IsShared,
            IsOwner = data.List.IsOwner,
        };

        foreach (GraphTodoTask task in data.Tasks)
        {
            if (!options.IncludeCompleted && IsCompleted(task.Status))
            {
                continue;
            }

            list.Tasks.Add(MapTask(snapshot, data.List, task, options));
        }

        return list;
    }

    private static ImportTask MapTask(
        ImportSnapshot snapshot,
        GraphTodoList list,
        GraphTodoTask source,
        MicrosoftTodoMapOptions options)
    {
        DateTimeOffset created = source.CreatedDateTime?.ToUniversalTime() ?? options.Now;
        DateTimeOffset modified = source.LastModifiedDateTime?.ToUniversalTime() ?? created;
        bool completed = IsCompleted(source.Status);
        var properties = new List<IcalExtensionProperty>
        {
            new(IdProperty, source.Id),
            new(ListIdProperty, list.Id),
        };

        var task = new TodoTask
        {
            Summary = string.IsNullOrWhiteSpace(source.Title) ? options.UntitledTaskTitle : source.Title.Trim(),
            Status = MapStatus(source.Status),
            Priority = source.Importance?.Trim().ToLowerInvariant() switch
            {
                "high" => 1,
                "low" => 9,
                _ => 0,
            },
            Created = created,
            LastModified = modified,
        };

        string? status = source.Status?.Trim();
        if (status is not null
            && !string.Equals(status, "notStarted", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "inProgress", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add(new(StatusProperty, status));
        }

        if (completed)
        {
            task.PercentComplete = 100;
            task.CompletedAt = GraphDateConverter.ToInstant(source.CompletedDateTime) ?? modified;
        }

        MapBody(source.Body, task, properties);

        if (GraphDateConverter.ToDate(source.DueDateTime, options.LocalTimeZone) is DateOnly due)
        {
            task.Due = new DateTimeOffset(due.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            task.DueIsDateOnly = true;
        }

        if (GraphDateConverter.ToDate(source.StartDateTime, options.LocalTimeZone) is DateOnly start)
        {
            task.Start = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            task.StartIsDateOnly = true;
        }

        if (source.Recurrence is not null)
        {
            if (PatternedRecurrenceConverter.TryConvert(source.Recurrence, out string? rrule))
            {
                task.RRule = rrule;
            }
            else
            {
                properties.Add(new(
                    RecurrenceProperty,
                    JsonSerializer.Serialize(source.Recurrence, GraphJsonContext.Default.GraphPatternedRecurrence)));
            }
        }

        foreach (string category in source.Categories ?? [])
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                task.Categories.Add(category.Trim());
            }
        }

        foreach (GraphLinkedResource link in source.LinkedResources ?? [])
        {
            if (task.Url is null && IsWebUrl(link.WebUrl))
            {
                task.Url = link.WebUrl!.Trim();
            }

            properties.Add(new(
                LinkedResourceProperty,
                JsonSerializer.Serialize(link, GraphJsonContext.Default.GraphLinkedResource)));
        }

        if (source.IsReminderOn && GraphDateConverter.ToInstant(source.ReminderDateTime) is DateTimeOffset reminder)
        {
            task.Alarms.Add(new TaskAlarm
            {
                Uid = ImportIdentity.DeterministicUid(
                    Provider,
                    snapshot.AccountKey,
                    ImportEntityType.Task,
                    source.Id + "/reminder"),
                Action = AlarmAction.Display,
                Trigger = reminder,
                // Never fire a burst of old reminders right after importing.
                IsAcknowledged = completed || reminder <= options.Now,
            });
        }

        Dictionary<string, JsonElement>? extra = source.AdditionalData?
            .Where(pair => !pair.Key.StartsWith('@'))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (extra is { Count: > 0 })
        {
            properties.Add(new(
                ExtraProperty,
                JsonSerializer.Serialize(extra, GraphJsonContext.Default.DictionaryStringJsonElement)));
        }

        task.IcalUnknown = IcalExtensionProperties.Merge(null, properties, []);

        var item = new ImportTask { ExternalId = source.Id, Task = task };
        foreach (GraphChecklistItem step in source.ChecklistItems ?? [])
        {
            if (!string.IsNullOrWhiteSpace(step.Id))
            {
                item.Steps.Add(MapStep(step, created, modified, options));
            }
        }

        if (options.IncludeAttachments)
        {
            foreach (GraphAttachment attachment in source.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.Id))
                {
                    continue;
                }

                item.Attachments.Add(new ImportAttachment
                {
                    ExternalId = attachment.Id,
                    FileName = string.IsNullOrWhiteSpace(attachment.Name) ? "attachment" : attachment.Name.Trim(),
                    MimeType = attachment.ContentType,
                    SizeBytes = Math.Max(0, attachment.Size),
                    SourcePath = string.Join('\n', list.Id, source.Id, attachment.Id),
                });
            }
        }

        return item;
    }

    private static ImportTask MapStep(
        GraphChecklistItem source,
        DateTimeOffset parentCreated,
        DateTimeOffset parentModified,
        MicrosoftTodoMapOptions options)
    {
        DateTimeOffset created = source.CreatedDateTime?.ToUniversalTime() ?? parentCreated;
        var task = new TodoTask
        {
            Summary = string.IsNullOrWhiteSpace(source.DisplayName) ? options.UntitledTaskTitle : source.DisplayName.Trim(),
            Status = source.IsChecked ? TaskStatus.Completed : TaskStatus.NeedsAction,
            Created = created,

            // Checklist items carry no modified time; edits to them modify the parent task.
            LastModified = parentModified,
        };
        if (source.IsChecked)
        {
            task.PercentComplete = 100;
            task.CompletedAt = source.CheckedDateTime?.ToUniversalTime() ?? parentModified;
        }

        return new ImportTask { ExternalId = source.Id, Task = task };
    }

    private static void MapBody(GraphItemBody? body, TodoTask task, List<IcalExtensionProperty> properties)
    {
        if (string.IsNullOrWhiteSpace(body?.Content))
        {
            return;
        }

        if (string.Equals(body.ContentType, "html", StringComparison.OrdinalIgnoreCase))
        {
            string text = HtmlNoteConverter.ToPlainText(body.Content);
            task.Description = text.Length == 0 ? null : text;
            properties.Add(new(
                HtmlBodyProperty,
                body.Content,
                [new KeyValuePair<string, string>("FMTTYPE", "text/html")]));
            return;
        }

        string plain = body.Content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        task.Description = plain.Length == 0 ? null : plain;
    }

    private static TaskStatus MapStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "completed" => TaskStatus.Completed,
            "inprogress" => TaskStatus.InProcess,
            _ => TaskStatus.NeedsAction,
        };
    }

    private static bool IsCompleted(string? status)
    {
        return string.Equals(status?.Trim(), "completed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebUrl(string? value)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}
