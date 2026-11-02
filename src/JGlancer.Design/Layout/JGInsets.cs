using Avalonia;

namespace JGlancer.Design;

/// <summary>Named paddings and margins built from <see cref="JGSpacing"/>, for XAML (<c>{x:Static d:JGInsets.…}</c>).</summary>
public static class JGInsets
{
    /// <summary>A 1-unit outline on every side (secondary buttons, fields, menus).</summary>
    public static Thickness Outline { get; } = new(JGMetrics.OutlineWidth);

    /// <summary>The viewer's file name between the back button and the position.</summary>
    public static Thickness ViewerName { get; } = new(JGSpacing.M, 0, JGSpacing.S, 0);

    /// <summary>The photo viewer's ‹ › from the window edges.</summary>
    public static Thickness PageButton { get; } = new(JGSpacing.M, 0);

    /// <summary>The players' scrubber and controls panel.</summary>
    public static Thickness PlayerControls { get; } = new(JGSpacing.M, JGSpacing.Xs);

    /// <summary>The audio player's waveform and controls (no panel behind them).</summary>
    public static Thickness AudioControls { get; } = new(JGSpacing.L, JGSpacing.S, JGSpacing.L, JGSpacing.M);

    /// <summary>The time label after the transport buttons.</summary>
    public static Thickness PlayerTime { get; } = new(JGSpacing.Xs, 0, 0, 0);

    /// <summary>A selection or drop-target edge.</summary>
    public static Thickness SelectionEdge { get; } = new(JGMetrics.SelectionEdgeWidth);

    /// <summary>Inside a drop-down menu, around its items.</summary>
    public static Thickness MenuPopup { get; } = new(JGSpacing.Xxs);

    /// <summary>The preview pane's extended details under its facts.</summary>
    public static Thickness PreviewDetails { get; } = new(0, JGSpacing.Xs, 0, 0);

    /// <summary>A flag star after a file name that may wrap (the preview pane).</summary>
    public static Thickness FlagBesideName { get; } = new(JGSpacing.Xs, JGSpacing.Xxs, 0, 0);

    /// <summary>Inside the top bar's search capsule.</summary>
    public static Thickness SearchField { get; } = new(JGSpacing.Xs, 0);

    /// <summary>The app icon in the title bar.</summary>
    public static Thickness TitleBarIcon { get; } = new(JGSpacing.S, 0, JGSpacing.Xs, 0);

    /// <summary>A sidebar section header row: label on the left, a compact button on the right.</summary>
    public static Thickness SidebarHeader { get; } = new(JGSpacing.M, JGSpacing.S, JGSpacing.Xs, 0);

    /// <summary>Rows inside the sidebar (folders, Trash).</summary>
    public static Thickness SidebarRow { get; } = new(JGSpacing.Xs, 0);

    /// <summary>The Trash row at the bottom of the sidebar (macOS: padding xs around it).</summary>
    public static Thickness SidebarFooter { get; } = new(0, JGSpacing.Xxs);

    /// <summary>Bars (top bar, status bar): horizontal padding.</summary>
    public static Thickness Bar { get; } = new(JGSpacing.M, 0);

    /// <summary>Large empty states.</summary>
    public static Thickness EmptyState { get; } = new(JGSpacing.Xxl);

    /// <summary>The actions row of an empty state sits a little lower than the text.</summary>
    public static Thickness EmptyStateActions { get; } = new(0, JGSpacing.Xs, 0, 0);

    /// <summary>Inside the preview pane and Quick Preview.</summary>
    public static Thickness Preview { get; } = new(JGSpacing.M);

    /// <summary>Inside the Get Info window (macOS: l around, m more on top).</summary>
    public static Thickness Info { get; } = new(JGSpacing.L, JGSpacing.L + JGSpacing.M, JGSpacing.L, JGSpacing.L);

    /// <summary>The pane's "PREVIEW" row above the player.</summary>
    public static Thickness PreviewHeader { get; } = new(0, 0, 0, JGSpacing.M);

    /// <summary>The file's name and facts under the pane's player.</summary>
    public static Thickness PreviewFacts { get; } = new(0, JGSpacing.M, 0, 0);

    /// <summary>Quick Preview's name row: clear of the window's resize edge, not of caption buttons (it has none).</summary>
    public static Thickness QuickPreviewName { get; } = new(JGSpacing.M, JGSpacing.Xs, JGSpacing.M, 0);

    /// <summary>Compare: each photo's caption, inset from the corner of its half.</summary>
    public static Thickness CompareCaption { get; } = new(JGSpacing.M);

    /// <summary>A small label on a scrim over a picture.</summary>
    public static Thickness Badge { get; } = new(JGSpacing.Xs, JGSpacing.Xxs);

    /// <summary>A drop-down button: its label and its icon.</summary>
    public static Thickness Button { get; } = new(JGSpacing.S, 0);
}
