using Wintodo.Core.Models;

namespace Wintodo.ViewModels;

// Unsubmitted step text lives for this editor session, never in another task's steps.
internal sealed class DetailStepDrafts
{
    private readonly Dictionary<string, (string Text, long Revision)> _drafts = new(StringComparer.Ordinal);
    private long _revision;

    internal string GetText(string uid) =>
        _drafts.TryGetValue(uid, out var draft) ? draft.Text : string.Empty;

    internal void SetText(string uid, string text)
    {
        if (text.Length == 0) _drafts.Remove(uid);
        else if (!_drafts.TryGetValue(uid, out var draft) || draft.Text != text)
            _drafts[uid] = (text, ++_revision);
    }

    internal async Task<TodoTask?> SubmitAsync(string uid, Func<string, string, Task<TodoTask>> addStep)
    {
        if (!_drafts.TryGetValue(uid, out var submitted) || string.IsNullOrWhiteSpace(submitted.Text))
            return null;

        var step = await addStep(uid, submitted.Text.Trim());
        // A new draft can even have the same text as the one currently saving.
        if (_drafts.TryGetValue(uid, out var current) && current.Revision == submitted.Revision)
            _drafts.Remove(uid);
        return step;
    }
}
