using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// Renaming in place, as Explorer and Finder do (macOS GridController+Rename): the caption becomes a field with the
/// name before the extension selected; Enter or a click elsewhere commits, Esc cancels.
/// </summary>
internal sealed partial class BrowserPane
{
    private TextBox? _renameField;
    private long _renameId;
    private string? _renamePath;

    /// <summary>Renames a file; returns a message when the name cannot be used (set by the window: file operations).</summary>
    public Func<string, string, Task<string?>>? RenameFile { get; set; }

    /// <summary>Shows a one-line message (set by the window: the toast).</summary>
    public Action<string>? ShowMessage { get; set; }

    public async void BeginRename(long assetId)
    {
        EndRename(commit: false);
        if (Model is not { } model || model.Order.IndexOf(assetId) is not { } index)
        {
            return;
        }
        var rows = await model.Store.AssetsAsync([assetId]);
        if (rows.Count == 0 || _renameField is not null)
        {
            return;
        }
        TileGrid.ScrollToVisible(index);
        var name = rows[0].Name;
        var field = new TextBox
        {
            Text = name,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            AcceptsReturn = false,
            MinHeight = JGMetrics.TileCaptionHeight,
            Padding = new Thickness(JGSpacing.Xxs, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(field, Core.Localization.L.T("Rename"));
        field.AddHandler(KeyDownEvent, OnRenameKey, RoutingStrategies.Tunnel);
        field.LostFocus += (_, _) => EndRename(commit: true);
        _renameField = field;
        _renameId = assetId;
        _renamePath = rows[0].Path;
        RenameLayer.Children.Add(field);
        PositionRename();
        TileGrid.ScrollInvalidated += OnGridMoved;
        TileGrid.SizeChanged += OnGridMoved;
        // Once it is laid out: focus, and select the name without its extension.
        Dispatcher.UIThread.Post(() =>
        {
            if (_renameField != field)
            {
                return;
            }
            field.Focus();
            field.SelectionStart = 0;
            field.SelectionEnd = Path.GetFileNameWithoutExtension(name).Length;
        }, DispatcherPriority.Loaded);
    }

    private void OnGridMoved(object? sender, EventArgs e) => PositionRename();

    /// <summary>Under the tile's square, a little wider than the tile, as tall as the wrapped name.</summary>
    private void PositionRename()
    {
        if (_renameField is not { } field || Model?.Order.IndexOf(_renameId) is not { } index)
        {
            return;
        }
        var geometry = TileGrid.Geometry;
        var frame = geometry.Frame(index);
        var top = frame.Y - TileGrid.Offset.Y + geometry.TileSide + (geometry.CaptionHeight > 0 ? 0 : -JGMetrics.TileCaptionHeight);
        if (TileGrid.TranslatePoint(new Point(frame.X - JGMetrics.BadgeInset, top), RenameLayer) is not { } at)
        {
            return;
        }
        field.Width = geometry.TileSide + 2 * JGMetrics.BadgeInset;
        Canvas.SetLeft(field, at.X);
        Canvas.SetTop(field, at.Y);
    }

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            EndRename(commit: e.Key == Key.Enter);
        }
    }

    private async void EndRename(bool commit)
    {
        if (_renameField is not { } field)
        {
            return;
        }
        _renameField = null;
        TileGrid.ScrollInvalidated -= OnGridMoved;
        TileGrid.SizeChanged -= OnGridMoved;
        var name = field.Text ?? "";
        var path = _renamePath;
        RenameLayer.Children.Remove(field);
        TileGrid.Focus();
        if (commit && path is not null && name != Path.GetFileName(path) && RenameFile is { } rename
            && await rename(path, name) is { } problem)
        {
            ShowMessage?.Invoke(problem);
        }
    }
}
