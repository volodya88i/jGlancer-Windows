using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Actions;

/// <summary>
/// The extended facts of one file (macOS MediaDetailsSection): read off the UI thread when the file changes, shown as
/// labels in the label style and values in mono, in two columns; nothing while reading or when there are none. A song
/// ends with "Key · BPM" (MAC-028, D-122): <c>A min · 128</c> when found before, else a lower-case <c>detect</c> in the
/// accent that finds them on a click (… while working, — when nothing is found); the row follows the audio player
/// finding them meanwhile.
/// </summary>
internal sealed class MediaDetailsSection : ContentControl
{
    private CancellationTokenSource? _reading;
    private string? _path;

    private Button? _detect;
    private TextBlock? _music;
    private MediaKind _kind;

    public MediaDetailsSection()
    {
        IsVisible = false;
    }

    /// <summary>Reads and shows <paramref name="path"/>'s details; null clears them.</summary>
    public void Show(string? path, MediaKind kind)
    {
        if (path == _path)
        {
            return;
        }
        _path = path;
        _kind = kind;
        _reading?.Cancel();
        Content = null;
        IsVisible = false;
        if (path is null || IsCloudOnly(path))
        {
            // Nothing read from a file kept only in the cloud: that would download it (MAC-040).
            return;
        }
        var stop = new CancellationTokenSource();
        _reading = stop;
        _ = LoadAsync(path, kind, stop.Token);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Viewer.Media.AudioAnalyzer.Found += OnAnalysisFound;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Viewer.Media.AudioAnalyzer.Found -= OnAnalysisFound;
        _reading?.Cancel();
        _path = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The file's content is only in the cloud (its attributes say so; reading them downloads nothing).</summary>
    internal static bool IsCloudOnly(string path)
    {
        try
        {
            return ((uint)File.GetAttributes(path) & Core.Scanning.DirectoryEntry.CloudOnlyAttributes) != 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>"A min · 128" (either part may be missing); null when nothing is known.</summary>
    internal static string? MusicLabel(Core.Audio.AudioAnalysis? found) =>
        found is null ? null : string.Join(" · ", new[] { found.Key?.Label, found.BpmLabel }.OfType<string>()) is { Length: > 0 } text ? text : null;

    private static async Task<Core.Audio.AudioAnalysis?> KnownAnalysisAsync(string path)
    {
        try
        {
            return await Viewer.Media.AudioAnalyzer.KnownAsync(path);
        }
        catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            return null;
        }
    }

    /// <summary>The audio player (or another view) found this song's key and tempo: the row shows them.</summary>
    private void OnAnalysisFound(object? sender, string path)
    {
        if (_kind == MediaKind.Audio && _music is not null && _path is { } shown && string.Equals(shown, path, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshMusicAsync(shown);
        }
    }

    private async Task RefreshMusicAsync(string path)
    {
        var label = MusicLabel(await KnownAnalysisAsync(path));
        if (label is not null && _path == path)
        {
            ShowMusic(label, null);
        }
    }

    /// <summary>A click on "detect": "…" while it works, then the value or "—" with a tooltip.</summary>
    private async Task DetectAsync(string path)
    {
        ShowMusic("…", null);
        Core.Audio.AudioAnalysis? found;
        try
        {
            found = await Viewer.Media.AudioAnalyzer.AnalyzeAsync(path, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            found = null;
        }
        if (_path != path)
        {
            return;
        }
        ShowMusic(MusicLabel(found) ?? "—", found is null ? L.T("Couldn't find the tempo or key of this file.") : null);
    }

    private void ShowMusic(string text, string? tip)
    {
        if (_music is null || _detect is null)
        {
            return;
        }
        _detect.IsVisible = false;
        _music.IsVisible = true;
        _music.Text = text;
        ToolTip.SetTip(_music, tip);
    }

    private async Task LoadAsync(string path, MediaKind kind, CancellationToken cancellation)
    {
        MediaDetails details;
        try
        {
            details = await MediaDetailsReader.ReadAsync(path, kind, cancellation);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cancellation.IsCancellationRequested)
        {
            return;
        }
        var rows = MediaDetailsRows.Rows(details);
        string? music = null;
        if (kind == MediaKind.Audio)
        {
            music = MusicLabel(await KnownAnalysisAsync(path));
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
        }
        _music = null;
        _detect = null;
        var grid = rows.Count == 0 && kind != MediaKind.Audio ? null : Grid(rows);
        if (grid is not null && kind == MediaKind.Audio)
        {
            AddMusicRow(grid, path, music);
        }
        Content = grid;
        IsVisible = grid is not null;
    }

    /// <summary>The last row of a song's details: "Key · BPM", the values or <c>detect</c>.</summary>
    private void AddMusicRow(Grid grid, string path, string? known)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var label = Text(L.T("Key · BPM").ToUpper(CultureInfo.CurrentUICulture), "label", JGColor.TextTertiary, selectable: false);
        label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _music = Text(known ?? "", "mono", JGColor.TextPrimary, selectable: true);
        _music.IsVisible = known is not null;
        var word = Text(L.T("detect"), "mono", JGColor.AccentText, selectable: false);
        _detect = new Button
        {
            Content = word,
            Classes = { "tertiary" },
            Padding = new Thickness(0),
            MinHeight = 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            IsVisible = known is null,
        };
        ToolTip.SetTip(_detect, L.T("Find the key and tempo"));
        Avalonia.Automation.AutomationProperties.SetName(_detect, L.T("Find the key and tempo"));
        _detect.Click += (_, _) => _ = DetectAsync(path);
        var value = new Panel { Children = { _music, _detect } };
        Avalonia.Controls.Grid.SetRow(label, row);
        Avalonia.Controls.Grid.SetRow(value, row);
        Avalonia.Controls.Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);
    }

    private static Grid Grid(IReadOnlyList<(string Label, string Value)> rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = JGSpacing.S,
            RowSpacing = JGSpacing.Xxs,
        };
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // The label style is in capitals (DESIGN.md §4).
            var label = Text(rows[i].Label.ToUpper(CultureInfo.CurrentUICulture), "label", JGColor.TextTertiary, selectable: false);
            label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            var value = Text(rows[i].Value, "mono", JGColor.TextPrimary, selectable: true);
            Avalonia.Controls.Grid.SetRow(label, i);
            Avalonia.Controls.Grid.SetRow(value, i);
            Avalonia.Controls.Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        return grid;
    }

    private static TextBlock Text(string text, string style, JGColorToken color, bool selectable)
    {
        TextBlock block = selectable ? new SelectableTextBlock() : new TextBlock();
        block.Text = text;
        block.TextWrapping = TextWrapping.Wrap;
        block.Classes.Add(style);
        block.Bind(TextElement.ForegroundProperty, block.GetResourceObservable(color.ResourceKey));
        return block;
    }
}
