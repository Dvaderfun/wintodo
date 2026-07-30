using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wintodo.Controls;

public sealed partial class ResponsiveTitleSearch : UserControl
{
    private const double WideMinWindowWidth = 900;

    public ResponsiveTitleSearch()
    {
        InitializeComponent();
    }

    internal void UpdateWindowWidth(double width)
    {
        string stateName = width >= WideMinWindowWidth
            ? nameof(Wide)
            : nameof(Compact);
        VisualStateManager.GoToState(this, stateName, false);
    }
}
