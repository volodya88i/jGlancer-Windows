using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using JGlancer.Core.Audio;
using JGlancer.Core.Localization;
using JGlancer.Core.Playback;
using JGlancer.Design;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// The audio player's key and tempo button and its panel (W-107, W-108): the tempo, the key, and the pitch and tempo the
/// song is sped up or slowed down to. A panel, not a menu: it stays open while values change, and its controls stay in
/// place and keep the focus. Its rows never change: while a song is analyzed its BPM and key show moving bars, when
/// nothing was found a dash, so the panel keeps its size. Every row is one control high, labels and values in the same
/// text style, so they share a line; the buttons on the right share one column width.
/// </summary>
internal sealed class MusicPanel
{
    private readonly MediaViewerModel _model;
    private readonly Grid _rows = new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        ColumnSpacing = JGSpacing.S,
        Width = JGMetrics.MusicPanelWidth,
        Margin = new Thickness(JGSpacing.Xs),
    };
    private readonly TextBlock _bpm = Value();
    private readonly ProgressBar _bpmWorking = Working();
    private readonly TextBlock _key = Value();
    private readonly ProgressBar _keyWorking = Working();
    private readonly TextBox _pitch = Field(L.T("Target Pitch in Semitones"));
    private readonly Button _lower;
    private readonly Button _higher;
    private readonly TextBox _tempo = Field(L.T("Target Tempo in BPM"));
    private readonly Button _reset;
    private readonly Flyout _flyout;

    public MusicPanel(MediaViewerModel model)
    {
        _model = model;
        _lower = Step("−", L.T("A Semitone Lower"), () => StepPitch(-1));
        _higher = Step("+", L.T("A Semitone Higher"), () => StepPitch(1));
        _lower.Width = _higher.Width = JGMetrics.MusicStepWidth;
        var pitchActions = Actions();
        pitchActions.Children.AddRange([_lower, _higher]);
        _reset = Step(L.T("Reset"), L.T("Reset"), () => _model.Playback.ClearTarget());
        // As wide as − and + together, so the column's edges line up.
        _reset.Width = 2 * JGMetrics.MusicStepWidth + JGSpacing.Xxs;
        var tempoActions = Actions();
        tempoActions.Children.Add(_reset);

        _pitch.KeyDown += (_, e) => CommitOnEnter(e, _pitch, CommitPitch);
        _pitch.LostFocus += (_, _) => CommitPitch();
        _tempo.KeyDown += (_, e) => CommitOnEnter(e, _tempo, CommitTempo);
        _tempo.LostFocus += (_, _) => CommitTempo();
        Place(Label(L.T("BPM")), 0, 0);
        Place(new Panel { Children = { _bpm, _bpmWorking } }, 0, 1, 2);
        Place(Label(L.T("Key")), 1, 0);
        Place(new Panel { Children = { _key, _keyWorking } }, 1, 1, 2);
        Place(Label(L.T("Target Pitch")), 2, 0);
        Place(_pitch, 2, 1);
        Place(pitchActions, 2, 2);
        Place(Label(L.T("Target Tempo")), 3, 0);
        Place(_tempo, 3, 1);
        Place(tempoActions, 3, 2);
        _flyout = new Flyout { Content = _rows, Placement = PlacementMode.TopEdgeAlignedRight };
    }

    /// <summary>What the button says: <c>B min · 144 · +2</c>, as the song sounds now.</summary>
    public static string ButtonLabel(MediaViewerModel model)
    {
        if (model.IsAnalyzing)
        {
            return "…";
        }
        var heard = model.Playback.HeardSemitones;
        var pitch = Math.Abs(heard) < 0.02 ? null : PlaybackTarget.Label(heard);
        if (model.Analysis is not { } found)
        {
            return pitch ?? L.T("Key · BPM");
        }
        // The key names the distance itself when it is not whole.
        var key = KeyAsHeard(found.Key, heard);
        var parts = new[] { key, HeardBpm(model) is { } bpm ? Whole(bpm) : null, PlaybackTarget.IsWhole(heard) || key is null ? pitch : null };
        return string.Join(" · ", parts.Where(part => part is not null));
    }

    /// <summary>Opens the panel; a song not analyzed yet is analyzed now.</summary>
    public void Show(Control anchor)
    {
        if (_model.Analysis is null && !_model.IsAnalyzing)
        {
            _model.Analyze();
        }
        Update();
        _flyout.ShowAt(anchor);
    }

    /// <summary>The panel over <paramref name="host"/>'s cell at the player's right edge, not in a popup (developer snapshots).</summary>
    public void ShowIn(Control host)
    {
        _flyout.Content = null;
        var frame = new Border
        {
            Child = _rows,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(JGSpacing.M),
        };
        if (Application.Current?.TryFindResource("JG.menuPopup", out var theme) == true && theme is ControlTheme popup)
        {
            frame.Theme = popup;
        }
        Grid.SetRow(frame, Grid.GetRow(host));
        ((Panel)host.Parent!).Children.Add(frame);
        _model.Analyze();
        Update();
    }

    /// <summary>Shows the model's values in the same rows: moving bars while analyzing, a dash where nothing was found.</summary>
    public void Update()
    {
        var playback = _model.Playback;
        var heard = playback.HeardSemitones;
        var found = _model.Analysis;
        var analyzing = _model.IsAnalyzing;
        _bpmWorking.IsVisible = _keyWorking.IsVisible = analyzing;
        _bpm.IsVisible = _key.IsVisible = !analyzing;
        var nothing = L.T("Couldn't find the tempo or key of this file.");

        var bpm = found?.Bpm;
        _bpm.Text = found?.BpmLabel ?? Missing;
        ToolTip.SetTip(_bpm, bpm is null ? nothing : null);

        var key = found?.Key;
        var asHeard = KeyAsHeard(key, heard);
        _key.Text = key is not { } shown ? Missing : asHeard == shown.Label ? asHeard : PlaybackTarget.IsWhole(heard) ? $"{shown.Label} → {asHeard}" : asHeard;
        ToolTip.SetTip(_key, key is null ? nothing : null);

        // Not while typing: playback reports would overwrite the digits.
        if (!_pitch.IsKeyboardFocusWithin)
        {
            _pitch.Text = PlaybackTarget.Label(heard);
        }
        // Without a tempo there is nothing to aim at: the field stays, empty and off.
        _tempo.IsEnabled = bpm is not null;
        if (!_tempo.IsKeyboardFocusWithin)
        {
            _tempo.Text = HeardBpm(_model) is { } now ? Whole(now) : "";
        }
        _lower.IsEnabled = heard > -PlaybackTarget.Limit + 0.5;
        _higher.IsEnabled = heard < PlaybackTarget.Limit - 0.5;
        _reset.IsEnabled = playback.IsTargeted;
    }

    private const string Missing = "—";

    /// <summary>A moving bar in a value's place while the song is analyzed.</summary>
    private static ProgressBar Working() => new()
    {
        IsIndeterminate = true,
        Height = JGMetrics.SliderThickness,
        MinHeight = 0,
        MinWidth = 0,
        Width = JGMetrics.MusicShiftFieldWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    /// <summary>The key sounding now: named when the pitch moved by whole semitones, else marked with the distance.</summary>
    private static string? KeyAsHeard(MusicKey? key, double heard)
    {
        if (key is not { } own)
        {
            return null;
        }
        return PlaybackTarget.IsWhole(heard) ? own.Shifted((int)Math.Round(heard)).Label : $"{own.Label} {PlaybackTarget.Label(heard)}";
    }

    /// <summary>The tempo sounding now.</summary>
    private static double? HeardBpm(MediaViewerModel model) => model.Analysis?.Bpm * model.Playback.Rate;

    private static string Whole(double bpm) => Math.Round(bpm).ToString("0", CultureInfo.InvariantCulture);

    private static void CommitOnEnter(KeyEventArgs e, TextBox field, Action commit)
    {
        if (e.Key == Key.Enter)
        {
            commit();
            field.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Goes to the typed pitch; anything unreadable goes back. Text left as it was changes nothing.</summary>
    private void CommitPitch()
    {
        var playback = _model.Playback;
        if (_pitch.Text != PlaybackTarget.Label(playback.HeardSemitones) && PlaybackTarget.TryParseSemitones(_pitch.Text, out var semitones))
        {
            playback.SetTargetRate(PlaybackTarget.RateForSemitones(semitones));
        }
        _pitch.Text = PlaybackTarget.Label(playback.HeardSemitones);
    }

    /// <summary>Goes to the typed tempo; anything unreadable goes back. Text left as it was changes nothing.</summary>
    private void CommitTempo()
    {
        if (_model.Analysis?.Bpm is not { } own || HeardBpm(_model) is not { } now)
        {
            return;
        }
        if (_tempo.Text != Whole(now) && PlaybackTarget.TryParseBpm(_tempo.Text, out var bpm))
        {
            _model.Playback.SetTargetRate(PlaybackTarget.RateForBpm(bpm, own));
            now = own * _model.Playback.Rate;
        }
        _tempo.Text = Whole(now);
    }

    private void StepPitch(int by) =>
        _model.Playback.SetTargetRate(PlaybackTarget.RateForSemitones(Math.Round(_model.Playback.HeardSemitones) + by));

    private void Place(Control control, int row, int column, int columnSpan = 1)
    {
        // Text sits centred in a box a control high, so text-only rows are as tall as the rows with fields and buttons,
        // and a label shares its line with the field beside it. The box shows and hides with its text.
        if (control is TextBlock text)
        {
            var box = new Panel { Height = JGMetrics.ControlHeightCompact, Children = { text } };
            box[!Visual.IsVisibleProperty] = text[!Visual.IsVisibleProperty];
            control = box;
        }
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
        // Not the grid's row spacing: it would leave gaps for hidden rows.
        control.Margin = new Thickness(0, JGSpacing.Xxs);
        _rows.Children.Add(control);
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextAlignment = Avalonia.Media.TextAlignment.Left };
        block.Classes.Add("body");
        block.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, block.GetResourceObservable("JG.text.secondary"));
        return block;
    }

    /// <summary>A value beside a label: the label's text style, so both sit on one line.</summary>
    private static TextBlock Value()
    {
        var block = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        block.Classes.Add("body");
        block.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, block.GetResourceObservable("JG.text.primary"));
        return block;
    }

    private static TextBox Field(string name)
    {
        var field = new TextBox
        {
            Width = JGMetrics.MusicShiftFieldWidth,
            MinHeight = JGMetrics.ControlHeightCompact,
            Height = JGMetrics.ControlHeightCompact,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = default,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };
        // A one-line field: no scroll bar (its arrow showed beside the digits).
        ScrollViewer.SetVerticalScrollBarVisibility(field, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(field, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        field.Classes.Add("body");
        Avalonia.Automation.AutomationProperties.SetName(field, name);
        ToolTip.SetTip(field, name);
        return field;
    }

    private static StackPanel Actions() => new() { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xxs, HorizontalAlignment = HorizontalAlignment.Right };

    private static Button Step(string text, string name, Action click)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = JGMetrics.ControlHeightCompact,
            Height = JGMetrics.ControlHeightCompact,
            MinWidth = JGMetrics.ControlHeightCompact,
            Padding = new Thickness(JGSpacing.Xs, 0),
        };
        ToolTip.SetTip(button, name);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }
}
