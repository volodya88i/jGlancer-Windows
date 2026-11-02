using System.Globalization;
using Avalonia.Controls;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Mpv;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// Audio tracks and subtitles of a video (W-88). With two or more audio tracks a button lists them: a
/// click plays that one; Play Together mixes any of them at once (a film's dialogue with its commentary, a concert's
/// two microphones). With subtitles (in the file, or a subtitle file named like the video beside it) a button lists
/// them with Off. The lists are read from mpv once the file is open.
/// </summary>
internal sealed partial class MediaViewerView
{
    private IReadOnlyList<MpvTrack> _tracks = [];
    private string? _tracksFor;

    /// <summary>Reads the file's tracks once it is open; the buttons show only when there is a choice.</summary>
    private void UpdateTracks()
    {
        if (_model is not { Kind: MediaKind.Video } model || model.Playback.Status != PlaybackStatus.Ready)
        {
            AudioTracksButton.IsVisible = SubtitlesButton.IsVisible = false;
            if (_model?.Current?.Path != _tracksFor)
            {
                _tracks = [];
                _tracksFor = null;
            }
            return;
        }
        if (model.Current?.Path is { } path && path != _tracksFor)
        {
            _tracksFor = path;
            _tracks = [];
            _ = ReadTracksAsync(model, path);
        }
        AudioTracksButton.IsVisible = _tracks.Count(t => t.Type == "audio") > 1;
        SubtitlesButton.IsVisible = _tracks.Any(t => t.Type == "sub");
    }

    /// <summary>The list comes off the UI thread: the file is just starting, and mpv waits for the UI to draw (W-100).</summary>
    private async Task ReadTracksAsync(MediaViewerModel model, string path)
    {
        var tracks = await model.Playback.Player.ReadAsync(player => player.Tracks());
        if (tracks is not null && ReferenceEquals(model, _model) && path == _tracksFor)
        {
            _tracks = tracks;
            UpdateTracks();
        }
    }

    private void ShowAudioMenu()
    {
        if (_model is not { } model)
        {
            return;
        }
        var player = model.Playback.Player;
        var audio = _tracks.Where(t => t.Type == "audio").ToList();
        var playing = player.SelectedAudio;
        var menu = new MenuFlyout();
        for (var i = 0; i < audio.Count; i++)
        {
            var track = audio[i];
            var item = new MenuItem { Header = TrackTitle(track, i), ToggleType = MenuItemToggleType.Radio, IsChecked = playing is [var only] && only == track.Id };
            item.Click += (_, _) => player.SelectAudio([track.Id]);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var together = new MenuItem { Header = L.T("Play Together") };
        for (var i = 0; i < audio.Count; i++)
        {
            var track = audio[i];
            var item = new MenuItem { Header = TrackTitle(track, i), ToggleType = MenuItemToggleType.CheckBox, IsChecked = playing.Contains(track.Id) };
            item.Click += (_, _) =>
            {
                // Adds or takes away this track; at least one keeps playing.
                var chosen = playing.Contains(track.Id) ? playing.Where(id => id != track.Id).ToList() : [.. playing, track.Id];
                if (chosen.Count > 0)
                {
                    player.SelectAudio([.. audio.Select(t => t.Id).Where(chosen.Contains)]);
                }
            };
            together.Items.Add(item);
        }
        var all = new MenuItem { Header = L.T("All Tracks"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = playing.Count == audio.Count };
        all.Click += (_, _) => player.SelectAudio([.. audio.Select(t => t.Id)]);
        together.Items.Add(new Separator());
        together.Items.Add(all);
        menu.Items.Add(together);
        menu.ShowAt(AudioTracksButton);
    }

    private void ShowSubtitlesMenu()
    {
        if (_model is not { } model)
        {
            return;
        }
        var player = model.Playback.Player;
        var shown = player.SelectedSubtitle;
        var menu = new MenuFlyout();
        var off = new MenuItem { Header = L.T("Off|subtitles"), ToggleType = MenuItemToggleType.Radio, IsChecked = shown is null };
        off.Click += (_, _) => player.SelectSubtitle(null);
        menu.Items.Add(off);
        var subtitles = _tracks.Where(t => t.Type == "sub").ToList();
        for (var i = 0; i < subtitles.Count; i++)
        {
            var track = subtitles[i];
            var item = new MenuItem { Header = TrackTitle(track, i), ToggleType = MenuItemToggleType.Radio, IsChecked = shown == track.Id };
            item.Click += (_, _) => player.SelectSubtitle(track.Id);
            menu.Items.Add(item);
        }
        menu.ShowAt(SubtitlesButton);
    }

    /// <summary>`--tracks-check`: the track list, both audio tracks mixed, then one, a subtitle shown; logged; closes.</summary>
    public async Task RunTracksCheckAsync(string log)
    {
        var lines = new List<string>();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        if (_model is not { } model)
        {
            return;
        }
        var player = model.Playback.Player;
        string P(string name) => $"{name}={player.GetString(name)}";
        lines.Add($"buttons audio={AudioTracksButton.IsVisible} subtitles={SubtitlesButton.IsVisible}");
        var audio = _tracks.Where(t => t.Type == "audio").ToList();
        for (var i = 0; i < _tracks.Count; i++)
        {
            lines.Add($"track {_tracks[i].Type} {TrackTitle(_tracks[i], i)}");
        }
        lines.Add("first: " + string.Join(",", player.SelectedAudio) + " " + P("sid") + " " + P("sub-text"));
        player.SelectAudio([.. audio.Select(t => t.Id)]);
        await Task.Delay(TimeSpan.FromSeconds(1));
        lines.Add("mixed: " + string.Join(",", player.SelectedAudio) + " " + P("lavfi-complex") + " " + P("audio-params/channel-count") + " " + P("time-pos"));
        player.SelectAudio([audio[^1].Id]);
        await Task.Delay(TimeSpan.FromSeconds(1));
        lines.Add("one: " + string.Join(",", player.SelectedAudio) + " " + P("aid") + " " + P("lavfi-complex") + " " + P("time-pos"));
        player.SelectSubtitle(null);
        lines.Add("off: " + P("sid"));
        await File.WriteAllLinesAsync(log, lines);
        (Avalonia.Controls.TopLevel.GetTopLevel(this) as Window)?.Close();
    }

    /// <summary>"Track 2 · eng · Commentary · aac 5.1 · file".</summary>
    private static string TrackTitle(MpvTrack track, int index)
    {
        var parts = new List<string> { L.T("Track {0}", (index + 1).ToString(CultureInfo.CurrentCulture)) };
        if (track.Language is { } language)
        {
            parts.Add(language);
        }
        if (track.Title is { } title)
        {
            parts.Add(title);
        }
        if (track.Codec is { } codec)
        {
            parts.Add(track.Channels > 2 ? $"{codec} {track.Channels - 1}.1" : codec);
        }
        if (track.IsExternal)
        {
            parts.Add(L.T("file|subtitle"));
        }
        return string.Join(" · ", parts);
    }
}
