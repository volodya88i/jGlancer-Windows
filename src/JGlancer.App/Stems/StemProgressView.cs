using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Stems;

/// <summary>
/// The card at the bottom of the window while a song is being split (W-85, like the card of a folder being added):
/// the song, the percent and the time left, how many songs wait, a bar and Stop.
/// </summary>
internal sealed class StemProgressView : Border
{
    private readonly TextBlock _title = new() { Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { Classes = { "mono" } };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = JGMetrics.OutlineWidth * 2, MinHeight = 0 };
    private StemJobs? _jobs;

    public StemProgressView()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(JGSpacing.M, 0, JGSpacing.M, JGSpacing.L + JGMetrics.FooterHeight);
        MinWidth = JGMetrics.DialogWidth;
        MaxWidth = JGMetrics.ReadableTextWidth;
        CornerRadius = new CornerRadius(JGMetrics.RadiusLarge);
        BorderThickness = new Thickness(JGMetrics.OutlineWidth);
        ClipToBounds = true;
        BoxShadow = new BoxShadows(new BoxShadow { Blur = JGMetrics.ToastShadowRadius, Color = Color.FromArgb((byte)(255 * JGMetrics.ToastShadowOpacity), 0, 0, 0) });
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgRaised.ResourceKey).ToBinding();
        this[!BorderBrushProperty] = this.GetResourceObservable(JGColor.LineStrong.ResourceKey).ToBinding();
        _title[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        _detail[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextSecondary.ResourceKey).ToBinding();
        _bar[!ProgressBar.ForegroundProperty] = this.GetResourceObservable(JGColor.Accent.ResourceKey).ToBinding();
        _bar[!ProgressBar.BackgroundProperty] = this.GetResourceObservable(JGColor.LineHair.ResourceKey).ToBinding();
        var stop = new Button { Classes = { "tertiary" }, Content = L.T("Stop"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(stop, L.T("Stop Splitting"));
        ToolTip.SetTip(stop, L.T("Stop Splitting"));
        stop.Click += (_, _) => _jobs?.Stop();
        var grid = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = JGSpacing.S };
        grid.Children.Add(new StackPanel { Spacing = JGSpacing.Xxs, Children = { _title, _detail } });
        Avalonia.Controls.Grid.SetColumn(stop, 1);
        grid.Children.Add(stop);
        Child = new StackPanel { Spacing = JGSpacing.Xs, Margin = new Thickness(JGSpacing.M, JGSpacing.S), Children = { grid, _bar } };
        Support.WindowGlass.Frost(this, JGColor.BgRaised);
        IsVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    public StemJobs? Jobs
    {
        get => _jobs;
        set
        {
            if (_jobs is not null)
            {
                _jobs.Changed -= OnChanged;
            }
            _jobs = value;
            if (_jobs is not null)
            {
                _jobs.Changed += OnChanged;
            }
            Update();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (_jobs?.Current is not { } job)
        {
            IsVisible = false;
            return;
        }
        IsVisible = true;
        _title.Text = L.T("Splitting “{0}”…", Path.GetFileName(job.Song));
        ToolTip.SetTip(_title, job.Song);
        AutomationProperties.SetName(this, _title.Text);
        var parts = new List<string> { _jobs.Progress.ToString("P0", CultureInfo.CurrentCulture) };
        if (_jobs.Remaining is { } left)
        {
            parts.Add(L.T("{0} left", Core.Model.DurationFormat.Positional(Math.Ceiling(left.TotalSeconds)) ?? ""));
        }
        if (_jobs.Waiting > 0)
        {
            parts.Add(L.Plural("{0} more waiting", _jobs.Waiting));
        }
        _detail.Text = string.Join(" · ", parts);
        _bar.Value = _jobs.Progress;
    }
}
