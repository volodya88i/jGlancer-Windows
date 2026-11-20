using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using JGlancer.Design;

namespace JGlancer.App.Preview;

/// <summary>
/// Quick Preview's own layout for photos and videos (owner, 0.6.1): the picture takes the whole window under the name
/// row, keeping its shape; a video's controls lie over it in one row on a translucent bar — play, the timeline, the
/// volume icon (its slider in a bubble above it on hover) and full screen, without speed or time — on a translucent,
/// lightly blurred bar that fades out two seconds after the pointer leaves the window. A photo has no bar; the window's
/// name row does the same over both. Songs and image sequences keep the regular layout.
/// </summary>
internal sealed partial class MiniPlayer
{
    private bool _overlay;
    private Thickness? _ownMargin;
    private Popup? _volumeBubble;
    private DispatcherTimer? _bubbleClose;
    private DispatcherTimer? _barHide;
    private Support.BackdropBlur? _barBlur;

    /// <summary>Photos and videos lie under Quick Preview's bars (the window puts its name row over them too).</summary>
    public bool IsOverlay => _overlay;

    /// <summary>The overlay layout came or went.</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>The bars faded out (false) or came back (true): Quick Preview's name row follows.</summary>
    public event EventHandler<bool>? BarsShownChanged;

    private Grid Root => (Grid)Content!;

    /// <summary>Puts the controls over the picture (Quick Preview, photo or video) or back in their rows.</summary>
    private void ApplyOverlay(bool overlay, bool video)
    {
        if (overlay != _overlay)
        {
            _overlay = overlay;
            _ownMargin ??= Margin;
            Margin = overlay ? default : _ownMargin.Value;
            foreach (var layer in new Control[] { Stage, Placeholder, FailedMessage })
            {
                Grid.SetRowSpan(layer, overlay ? 3 : 1);
            }
            MoveScrubber(intoBar: overlay);
            MoveVolumeSlider(intoBubble: overlay);
            Controls.Background = overlay ? Translucent() : null;
            Controls.Margin = default;
            // Room inside the bar around its buttons.
            var groups = Controls.Children.OfType<StackPanel>().ToList();
            groups[0].Margin = overlay ? JGInsets.QuickPreviewBarStart : default;
            groups[^1].Margin = overlay ? JGInsets.QuickPreviewBarEnd : default;
            // A light frost of the picture behind the bar.
            if (_barBlur is null)
            {
                _barBlur = new Support.BackdropBlur { Radius = JGMetrics.BackdropBlurRadius };
                Grid.SetColumnSpan(_barBlur, 3);
                Controls.Children.Insert(0, _barBlur);
            }
            _barBlur.IsVisible = overlay;
            Controls.Transitions = overlay && Settings.Motion.Enabled ? [Fade()] : null;
            Controls.Opacity = 1;
            Controls.IsHitTestVisible = true;
            OverlayChanged?.Invoke(this, EventArgs.Empty);
            BarsShownChanged?.Invoke(this, true);
        }
        if (overlay)
        {
            Controls.IsVisible = video;
            TimeText.IsVisible = SpeedButton.IsVisible = false;
            VolumeSlider.IsVisible = true;
        }
    }

    /// <summary>The bar's background: the scrim, translucent, so the picture shows through it.</summary>
    private IBrush Translucent() =>
        this.Brush(JGColor.OverlayScrim) is ISolidColorBrush scrim
            ? new SolidColorBrush(scrim.Color, JGMetrics.QuickPreviewBarOpacity)
            : Brushes.Transparent;

    private void MoveScrubber(bool intoBar)
    {
        if (intoBar && Scrubber.Parent == Root)
        {
            Root.Children.Remove(Scrubber);
            Grid.SetRow(Scrubber, 0);
            Grid.SetColumn(Scrubber, 1);
            Scrubber.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Scrubber.Margin = JGInsets.QuickPreviewTimeline;
            Controls.Children.Add(Scrubber);
        }
        else if (!intoBar && Scrubber.Parent == Controls)
        {
            Controls.Children.Remove(Scrubber);
            Grid.SetColumn(Scrubber, 0);
            Grid.SetRow(Scrubber, 1);
            Scrubber.Margin = default;
            Root.Children.Add(Scrubber);
        }
    }

    /// <summary>The volume slider: in the row, or in a bubble above the volume icon that opens on hover.</summary>
    private void MoveVolumeSlider(bool intoBubble)
    {
        var row = (Panel)MuteButton.Parent!;
        if (intoBubble && VolumeSlider.Parent == row)
        {
            row.Children.Remove(VolumeSlider);
            // Upright in a small capsule like the status bubble: no room around it but its own padding.
            VolumeSlider.IsVertical = true;
            VolumeSlider.Width = JGMetrics.VolumeBubbleWidth;
            VolumeSlider.Height = JGMetrics.VolumeBubbleTrackHeight;
            var surface = new Border
            {
                Child = VolumeSlider,
                Padding = JGInsets.VolumeBubble,
                CornerRadius = new CornerRadius(JGMetrics.VolumeBubbleWidth / 2 + JGInsets.VolumeBubble.Left),
                BorderThickness = JGInsets.Outline,
            };
            surface[!Border.BackgroundProperty] = surface.GetResourceObservable(JGColor.BgRaised.ResourceKey,
                brush => brush is ISolidColorBrush raised ? new SolidColorBrush(raised.Color, JGMetrics.StatusBubbleTintOpacity) : brush).ToBinding();
            surface[!Border.BorderBrushProperty] = surface.GetResourceObservable(JGColor.LineHair.ResourceKey).ToBinding();
            _volumeBubble = new Popup
            {
                PlacementTarget = MuteButton,
                Placement = PlacementMode.Top,
                VerticalOffset = -JGSpacing.Xxs,
                Child = surface,
                IsLightDismissEnabled = false,
            };
            surface.PointerEntered += (_, _) =>
            {
                _bubbleClose?.Stop();
                ShowBar();
            };
            surface.PointerExited += (_, _) => CloseBubbleSoon();
            Root.Children.Add(_volumeBubble);
            MuteButton.PointerEntered += OnMuteEntered;
            MuteButton.PointerExited += OnMuteExited;
        }
        else if (!intoBubble && _volumeBubble is { } bubble)
        {
            bubble.IsOpen = false;
            ((Border)bubble.Child!).Child = null;
            Root.Children.Remove(bubble);
            VolumeSlider.IsVertical = false;
            VolumeSlider.Width = JGMetrics.VolumeSliderWidth;
            VolumeSlider.ClearValue(HeightProperty);
            _volumeBubble = null;
            MuteButton.PointerEntered -= OnMuteEntered;
            MuteButton.PointerExited -= OnMuteExited;
            row.Children.Insert(row.Children.IndexOf(MuteButton) + 1, VolumeSlider);
        }
    }

    private void OnMuteEntered(object? sender, PointerEventArgs e)
    {
        _bubbleClose?.Stop();
        if (_volumeBubble is { } bubble)
        {
            bubble.IsOpen = true;
        }
    }

    private void OnMuteExited(object? sender, PointerEventArgs e) => CloseBubbleSoon();

    /// <summary>A moment to reach the bubble from the icon before it closes.</summary>
    private void CloseBubbleSoon()
    {
        _bubbleClose ??= new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.VolumeBubbleCloseDelay), DispatcherPriority.Normal, (_, _) =>
        {
            _bubbleClose!.Stop();
            if (_volumeBubble is { } bubble)
            {
                bubble.IsOpen = false;
            }
        });
        _bubbleClose.Stop();
        _bubbleClose.Start();
    }

    // MARK: The bar fades out after the pointer leaves the window

    private void OnWindowPointerEntered(object? sender, PointerEventArgs e) => ShowBar();

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e) => ShowBar();

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_overlay)
        {
            return;
        }
        _barHide ??= new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.QuickPreviewBarHideDelay), DispatcherPriority.Normal, (_, _) =>
        {
            _barHide!.Stop();
            if (_overlay)
            {
                Controls.Opacity = 0;
                Controls.IsHitTestVisible = false;
                if (_volumeBubble is { } bubble)
                {
                    bubble.IsOpen = false;
                }
                BarsShownChanged?.Invoke(this, false);
            }
        });
        _barHide.Stop();
        _barHide.Start();
    }

    private void ShowBar()
    {
        _barHide?.Stop();
        if (_overlay && Controls.Opacity < 1)
        {
            Controls.Opacity = 1;
            Controls.IsHitTestVisible = true;
            BarsShownChanged?.Invoke(this, true);
        }
    }

    private static DoubleTransition Fade() => new()
    {
        Property = OpacityProperty,
        Duration = TimeSpan.FromSeconds(JGMetrics.FadeDuration),
        Easing = new CubicEaseOut(),
    };
}
