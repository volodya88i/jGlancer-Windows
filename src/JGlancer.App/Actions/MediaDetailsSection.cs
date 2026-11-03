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
/// labels in the label style and values in mono, in two columns; nothing while reading or when there are none.
/// </summary>
internal sealed class MediaDetailsSection : ContentControl
{
    private CancellationTokenSource? _reading;
    private string? _path;

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
        _reading?.Cancel();
        Content = null;
        IsVisible = false;
        if (path is null)
        {
            return;
        }
        var stop = new CancellationTokenSource();
        _reading = stop;
        _ = LoadAsync(path, kind, stop.Token);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _reading?.Cancel();
        _path = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Tempo and key found earlier, as rows.</summary>
    private static async Task<IEnumerable<(string, string)>?> KnownAnalysisAsync(string path)
    {
        try
        {
            if (await Viewer.Media.AudioAnalyzer.KnownAsync(path) is not { } found)
            {
                return null;
            }
            var rows = new List<(string, string)>();
            if (found.BpmLabel is { } bpm)
            {
                rows.Add((L.T("BPM"), bpm));
            }
            if (found.Key is { } key)
            {
                rows.Add((L.T("Key"), key.Label));
            }
            return rows;
        }
        catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            return null;
        }
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
        if (kind == MediaKind.Audio && await KnownAnalysisAsync(path) is { } known)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            rows = [.. rows, .. known];
        }
        Content = rows.Count == 0 ? null : Grid(rows);
        IsVisible = rows.Count > 0;
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
