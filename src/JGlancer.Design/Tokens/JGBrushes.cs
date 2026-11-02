using Avalonia.Controls;
using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>Token brushes for controls that draw themselves (grid, scrubber): the current theme's value of a token.</summary>
public static class JGBrushes
{
    /// <summary>The brush of <paramref name="token"/> in <paramref name="control"/>'s theme.</summary>
    public static IBrush Brush(this Control control, JGColorToken token)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(token);
        return control.TryFindResource(token.ResourceKey, control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
    }
}
