using Avalonia.Controls;

namespace JGlancer.App.Library;

/// <summary>The status bar under the grid.</summary>
internal sealed partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
        DismissButton.Click += (_, _) =>
        {
            if (DataContext is LibraryModel model)
            {
                model.Problem = LibraryProblem.None;
            }
        };
    }
}
