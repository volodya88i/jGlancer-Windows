using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>How flags look and read (macOS Flags.swift): the star's color, the menu titles, the menu order.</summary>
internal static class FlagStyle
{
    /// <summary>The flags of the menus in order: accent, the six colors, then Remove Flag.</summary>
    public static IReadOnlyList<AssetFlag> MenuOrder { get; } = [AssetFlag.Accent, .. AssetFlags.Colors, AssetFlag.None];

    public static JGColorToken Token(AssetFlag flag) => flag switch
    {
        AssetFlag.Red => JGColor.FlagRed,
        AssetFlag.Orange => JGColor.FlagOrange,
        AssetFlag.Yellow => JGColor.FlagYellow,
        AssetFlag.Green => JGColor.FlagGreen,
        AssetFlag.Blue => JGColor.FlagBlue,
        AssetFlag.Purple => JGColor.FlagPurple,
        _ => JGColor.AccentText,
    };

    public static string Title(AssetFlag flag) => flag switch
    {
        AssetFlag.None => L.T("Remove Flag"),
        AssetFlag.Accent => L.T("Accent Color"),
        AssetFlag.Red => L.T("Red"),
        AssetFlag.Orange => L.T("Orange"),
        AssetFlag.Yellow => L.T("Yellow"),
        AssetFlag.Green => L.T("Green"),
        AssetFlag.Blue => L.T("Blue"),
        _ => L.T("Purple"),
    };

    /// <summary>The filled star, parsed once (the Lucide outline, filled and stroked in one color).</summary>
    public static Geometry Star { get; } = StreamGeometry.Parse(JGIconData.Star);

    /// <summary>A filled star of <paramref name="size"/> in the flag's color (menus, beside names); an × for Remove Flag.</summary>
    public static Control Glyph(Control owner, AssetFlag flag, double size)
    {
        if (flag == AssetFlag.None)
        {
            return new JGIcon { Data = JGIconData.X, Width = size, Height = size, Foreground = owner.Brush(JGColor.TextSecondary) };
        }
        var brush = owner.Brush(Token(flag));
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Star,
                Fill = brush,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeJoin = PenLineJoin.Round,
                Width = 24,
                Height = 24,
            },
        };
    }

    /// <summary>Draws the star of <paramref name="flag"/> filling <paramref name="box"/> (the grid's badges).</summary>
    public static void Draw(DrawingContext context, IBrush brush, Rect box)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scale = box.Width / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X, box.Y)))
        {
            context.DrawGeometry(brush, new Pen(brush, 2, lineJoin: PenLineJoin.Round), Star);
        }
    }
}
