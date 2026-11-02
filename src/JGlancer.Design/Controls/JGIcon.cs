using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>
/// A Lucide stroke icon (W-03) drawn from <see cref="JGIconData"/> path data on a 24 × 24 canvas, scaled to the
/// control's size (default 16, the size of the adjacent 13 px text, DESIGN.md §10) in the inherited foreground.
/// </summary>
public sealed class JGIcon : Control
{
    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<JGIcon, string?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<JGIcon>();

    /// <summary>Stroke width on the 24-unit canvas; Lucide's own is 2.</summary>
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<JGIcon, double>(nameof(StrokeWidth), 2);

    /// <summary>A solid shape (the players' transport glyphs, as the Mac's SF Symbols ".fill"): filled, not stroked.</summary>
    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<JGIcon, bool>(nameof(IsFilled));

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    static JGIcon()
    {
        AffectsRender<JGIcon>(DataProperty, ForegroundProperty, StrokeWidthProperty, IsFilledProperty);
        WidthProperty.OverrideDefaultValue<JGIcon>(JGMetrics.IconSize);
        HeightProperty.OverrideDefaultValue<JGIcon>(JGMetrics.IconSize);
        IsHitTestVisibleProperty.OverrideDefaultValue<JGIcon>(false);
    }

    /// <summary>Path data, normally a <see cref="JGIconData"/> constant.</summary>
    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Data) || Foreground is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        var geometry = Geometry(Data);
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        // A filled shape has no edge of its own: its straight sides stay on whole pixels (W-99).
        var pen = IsFilled ? null : new Pen(Foreground, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var offset = new Vector((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            context.DrawGeometry(IsFilled ? Foreground : null, pen, geometry);
        }
    }

    private static Geometry Geometry(string data)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(data, out var geometry))
            {
                geometry = StreamGeometry.Parse(data);
                Cache[data] = geometry;
            }
            return geometry;
        }
    }
}
