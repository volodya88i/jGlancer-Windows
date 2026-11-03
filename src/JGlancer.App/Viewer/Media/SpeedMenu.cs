using System.Globalization;
using Avalonia.Controls;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;

namespace JGlancer.App.Viewer.Media;

/// <summary>The players' speed button menu (macOS SpeedMenu): the speeds, then Keep Pitch.</summary>
internal static class SpeedMenu
{
    /// <summary><c>1.0×</c>, <c>1.25×</c>, <c>0.5×</c> in the user's number format.</summary>
    public static string Label(double rate) => rate.ToString("0.0#", CultureInfo.CurrentCulture) + "×";

    /// <summary>The speeds that land on a pitch, in semitones from the song's own (W-108).</summary>
    private static MenuItem TargetPitch(MediaPlayback playback)
    {
        var target = new MenuItem { Header = L.T("Target Pitch") };
        for (var semitones = PlaybackTarget.Limit; semitones >= -PlaybackTarget.Limit; semitones--)
        {
            var chosen = semitones;
            var item = new MenuItem
            {
                Header = $"{PlaybackTarget.Label(chosen)} · {Label(PlaybackTarget.RateForSemitones(chosen))}",
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "target",
                IsChecked = playback.IsTargeted && Math.Abs(playback.HeardSemitones - chosen) < 0.02,
            };
            item.Click += (_, _) => playback.SetTargetRate(PlaybackTarget.RateForSemitones(chosen));
            target.Items.Add(item);
        }
        return target;
    }

    public static void Show(Control anchor, MediaPlayback playback)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        foreach (var rate in PlaybackSpeed.Choices(playback.Rate))
        {
            var item = new MenuItem { Header = Label(rate), ToggleType = MenuItemToggleType.Radio, GroupName = "speed", IsChecked = rate == playback.Rate };
            item.Click += (_, _) => playback.SetRate(rate);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var pitch = new MenuItem { Header = L.T("Keep Pitch"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = playback.PreservesPitch };
        pitch.Click += (_, _) => playback.SetPreservesPitch(!playback.PreservesPitch);
        menu.Items.Add(pitch);
        if (playback.Kind == MediaKind.Audio)
        {
            menu.Items.Add(TargetPitch(playback));
        }
        menu.ShowAt(anchor);
    }
}
