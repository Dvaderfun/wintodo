using Microsoft.UI.Xaml;
using Wintodo.Core.Models;

namespace Wintodo.Controls;

public static class CategoryIconStyles
{
    // The NavigationViewItem presenter supplies its own state-specific foreground.
    public static Style? ForNavigation(ListColor? color, bool isHighContrast) =>
        color is null || isHighContrast ? null : ForColor(color);

    // Each style leaves Foreground as a live ThemeResource on the icon itself.
    public static Style ForColor(ListColor? color) =>
        (Style)Application.Current.Resources[$"{color?.ToString() ?? "Neutral"}CategoryIconStyle"];
}
