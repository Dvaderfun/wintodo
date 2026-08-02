using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wintodo.Controls;

/// <summary>
/// A <see cref="TextBox"/> without the built-in clear button. At title size that glyph
/// sits in the middle of the header and there is no property to turn it off, so the
/// template part is neutralised here. Enter commits and Escape cancels instead.
/// </summary>
public sealed partial class TitleEditBox : TextBox
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("DeleteButton") is not FrameworkElement clearButton)
        {
            return;
        }

        // The template's visual states drive Visibility, so collapse it by size —
        // that survives the state changes that would otherwise re-show it.
        clearButton.MinWidth = 0;
        clearButton.Width = 0;
        clearButton.Opacity = 0;
        clearButton.IsHitTestVisible = false;
    }
}
