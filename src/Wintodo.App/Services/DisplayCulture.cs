using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Wintodo.Core.Presentation;

namespace Wintodo.Services;

internal static class DisplayCulture
{
    internal static CultureInfo Current { get; private set; } = CultureInfo.CurrentUICulture;

    internal static void Initialize()
    {
        Current = DisplayCultureResolver.Resolve(
            new ResourceLoader().GetString("App_FormattingCulture"),
            CultureInfo.CurrentUICulture);
    }
}
