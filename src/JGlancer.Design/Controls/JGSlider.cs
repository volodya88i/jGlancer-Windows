using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>
/// The plain slider (W-98): a rounded track, the part up to the value in <c>text.primary</c>, no thumb. Click or
/// drag (Shift: in steps), mouse wheel (a fifth of a step per notch; Shift: a step), ←/→ and ↑/↓ when focused. Being a <see cref="RangeBase"/>,
/// screen readers see an ordinary slider.
/// </summary>
public sealed class JGSlider : RangeBase
{
    private bool _dragging;

    static JGSlider()
    {
        FocusableProperty.OverrideDefaultValue<JGSlider>(true);
        HeightProperty.OverrideDefaultValue<JGSlider>(JGMetrics.ControlHeightCompact);
        AffectsRender<JGSlider>(ValueProperty, MinimumProperty, MaximumProperty);
        // No focus ring: the slider shows its value itself.
        FocusAdornerProperty.OverrideDefaultValue<JGSlider>(null);
    }

    public override void Render(DrawingContext context)
    {
        var span = Maximum - Minimum;
        // Avalonia hit-tests what is drawn: without this the pointer and the wheel only reach the thin track.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Bounds.Width <= 0 || span <= 0)
        {
            return;
        }
        JGSliderTrack.Draw(context, this, Bounds.Size, (Value - Minimum) / span);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _dragging = true;
        e.Pointer.Capture(this);
        Track(e.GetPosition(this).X, e.KeyModifiers);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            Track(e.GetPosition(this).X, e.KeyModifiers);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : -e.Delta.X;
        if (delta == 0)
        {
            return;
        }
        var step = SmallChange > 0 ? SmallChange : 1;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // To the next step that way (Shift on a slider that moves finely, as Shift with the mouse button).
            var ticks = (Value - Minimum) / step;
            var next = delta > 0 ? Math.Floor(ticks + 1e-9) + 1 : Math.Ceiling(ticks - 1e-9) - 1;
            Set(Minimum + next * step, snap: true);
        }
        else
        {
            // Fine: a fifth of a step per notch, as dragging sets any value.
            Set(Value + Math.Sign(delta) * Math.Max(1, step / FineSteps), snap: false);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var change = e.Key switch
        {
            Key.Left or Key.Down => -SmallChange,
            Key.Right or Key.Up => SmallChange,
            _ => 0,
        };
        if (change != 0)
        {
            Set(Value + change, snap: false);
            e.Handled = true;
        }
    }

    /// <summary>Shift held: in steps.</summary>
    private void Track(double x, KeyModifiers modifiers)
    {
        var fraction = Math.Clamp(x / Math.Max(Bounds.Width, 1), 0, 1);
        Set(Minimum + fraction * (Maximum - Minimum), snap: modifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary>Wheel notches per step when the slider moves finely.</summary>
    private const double FineSteps = 5;

    /// <summary>Clamps, and snaps to the step when <paramref name="snap"/> (Shift held); otherwise any whole value.</summary>
    private void Set(double value, bool snap)
    {
        var step = !snap ? 1 : SmallChange > 0 ? SmallChange : 1;
        var snapped = Minimum + Math.Round((value - Minimum) / step) * step;
        Value = Math.Clamp(snapped, Minimum, Maximum);
    }
}

/// <summary>How the slider and the plain scrubber draw their track (W-98).</summary>
internal static class JGSliderTrack
{
    /// <summary>The track across <paramref name="size"/>, filled up to <paramref name="fraction"/>.</summary>
    public static void Draw(DrawingContext context, Control control, Size size, double fraction)
    {
        var thickness = JGMetrics.SliderThickness;
        var radius = thickness / 2;
        var top = (size.Height - thickness) / 2;
        context.DrawRectangle(control.Brush(JGColor.LineStrong), null, new Rect(0, top, size.Width, thickness), radius, radius);
        var filled = Math.Clamp(fraction, 0, 1) * size.Width;
        if (filled > 0)
        {
            context.DrawRectangle(control.Brush(JGColor.TextPrimary), null, new Rect(0, top, Math.Max(filled, thickness), thickness), radius, radius);
        }
    }
}
