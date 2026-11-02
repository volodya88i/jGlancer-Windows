using Avalonia;
using Avalonia.Controls;

namespace JGlancer.App.Preview;

/// <summary>Lays its children out in the largest centered rectangle of <see cref="Aspect"/> (width / height) that fits.</summary>
internal sealed class AspectFitPanel : Panel
{
    public static readonly StyledProperty<double> AspectProperty =
        AvaloniaProperty.Register<AspectFitPanel, double>(nameof(Aspect), 16d / 9);

    static AspectFitPanel() => AffectsArrange<AspectFitPanel>(AspectProperty);

    public double Aspect
    {
        get => GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var aspect = Aspect > 0 && double.IsFinite(Aspect) ? Aspect : 16d / 9;
        var width = Math.Min(finalSize.Width, finalSize.Height * aspect);
        var height = width / aspect;
        var frame = new Rect((finalSize.Width - width) / 2, (finalSize.Height - height) / 2, width, height);
        foreach (var child in Children)
        {
            child.Arrange(frame);
        }
        return finalSize;
    }
}
