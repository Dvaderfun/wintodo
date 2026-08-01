namespace Wintodo.ViewModels;

public sealed record MoveTargetViewModel(long ListId, string Uid, string DisplayName)
{
    public string AutomationId => $"Todo.Row.MoveTo.{Uid}";

    public string ComposerAutomationId => $"Todo.Composer.List.{Uid}";
}
