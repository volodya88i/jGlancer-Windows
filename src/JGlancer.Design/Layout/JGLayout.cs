using Avalonia;
using Avalonia.Controls;

namespace JGlancer.Design;

/// <summary>Grid lengths built from <see cref="JGMetrics"/>, for XAML column and row definitions.</summary>
public static class JGLayout
{
    /// <summary>The sidebar column (220).</summary>
    public static GridLength SidebarColumn { get; } = new(JGMetrics.SidebarWidth);

    /// <summary>The photo viewer's round page buttons.</summary>
    public static CornerRadius PageButtonRadius { get; } = new(JGMetrics.PageButtonSide / 2);

    /// <summary>A hairline column between panes.</summary>
    public static GridLength Hairline { get; } = new(JGMetrics.OutlineWidth);

    /// <summary>The top bar's thumbnail-size slider: half a sidebar wide (macOS).</summary>
    public const double SliderWidth = JGMetrics.SidebarWidth / 2;
}
