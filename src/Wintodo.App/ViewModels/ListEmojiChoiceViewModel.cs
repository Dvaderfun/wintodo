using System.Windows.Input;

namespace Wintodo.ViewModels;

public sealed partial class ListEmojiChoiceViewModel
{
    internal ListEmojiChoiceViewModel(
        string emoji,
        string name,
        string automationId,
        ICommand selectCommand)
    {
        Emoji = emoji;
        Name = name;
        AutomationId = automationId;
        SelectCommand = selectCommand;
    }

    public string Emoji { get; }

    public string Name { get; }

    public string AutomationId { get; }

    public ICommand SelectCommand { get; }
}
