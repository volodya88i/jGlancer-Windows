using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>A library folder's own icon ready to draw (macOS D-129): a symbol's path, an emoji, or a decoded picture.</summary>
internal sealed record FolderIconImage(string? SymbolData, string? Emoji, Bitmap? Picture);

/// <summary>
/// A sidebar icon in its box (<see cref="JGMetrics.SidebarIconBox"/>, macOS FolderIconView): the folder's own symbol,
/// emoji or picture, else <see cref="Fallback"/> (the disk or the folder glyph). Symbols take <see cref="Foreground"/>;
/// an emoji is a little larger than the text; a picture fills the box with small rounded corners.
/// </summary>
internal sealed class FolderIconView : Decorator
{
    public static readonly StyledProperty<FolderIconImage?> IconProperty =
        AvaloniaProperty.Register<FolderIconView, FolderIconImage?>(nameof(Icon));

    public static readonly StyledProperty<string?> FallbackProperty =
        AvaloniaProperty.Register<FolderIconView, string?>(nameof(Fallback));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<FolderIconView, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<double> SideProperty =
        AvaloniaProperty.Register<FolderIconView, double>(nameof(Side), JGMetrics.SidebarIconBox);

    static FolderIconView()
    {
        IconProperty.Changed.AddClassHandler<FolderIconView>((view, _) => view.Update());
        FallbackProperty.Changed.AddClassHandler<FolderIconView>((view, _) => view.Update());
        ForegroundProperty.Changed.AddClassHandler<FolderIconView>((view, _) => view.Update());
        SideProperty.Changed.AddClassHandler<FolderIconView>((view, _) => view.Update());
    }

    public FolderIconView()
    {
        Update();
    }

    public FolderIconImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Fallback
    {
        get => GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The box's side (the picker's header shows a larger one).</summary>
    public double Side
    {
        get => GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    private void Update()
    {
        var side = Side;
        Width = Height = side;
        var scale = side / JGMetrics.SidebarIconBox;
        if (Icon?.Picture is { } picture)
        {
            Child = new Border
            {
                CornerRadius = new CornerRadius(JGMetrics.RadiusSmall * scale / 2),
                ClipToBounds = true,
                Child = new Image { Source = picture, Stretch = Stretch.UniformToFill },
            };
        }
        else if (Icon?.Emoji is { } emoji)
        {
            Child = new TextBlock
            {
                Text = emoji,
                FontFamily = JGFonts.Emoji,
                FontSize = (JGTypeScale.Body.Size + 1) * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            Child = new JGIcon
            {
                Data = Icon?.SymbolData ?? Fallback,
                Foreground = Foreground,
                Width = JGMetrics.IconSize * scale,
                Height = JGMetrics.IconSize * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }
}
