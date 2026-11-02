using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace JGlancer.Design;

/// <summary>Control themes of the design system (JGStyles.axaml).</summary>
public sealed partial class JGStyles : Styles
{
    public JGStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
