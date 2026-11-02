using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Actions;

/// <summary>
/// Get Info (macOS InfoController): the facts of one file with its extended details (camera, exposure, codecs; read
/// from the file), or the count and size of several.
/// </summary>
internal sealed partial class InfoWindow : Window
{
    private readonly MediaDetailsSection _details = new();

    public InfoWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    public void Fill(IReadOnlyList<Asset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        Rows.Children.Clear();
        if (assets.Count == 1)
        {
            var asset = assets[0];
            Rows.Children.Add(Text(asset.Name, "heading", "JG.text.primary", selectable: true));
            Row(L.T("Size"), ByteFormat.Format(asset.Size));
            if (asset.Width is { } width && asset.Height is { } height)
            {
                Row(L.T("Dimensions"), string.Create(CultureInfo.InvariantCulture, $"{width} × {height}"));
            }
            if (asset.Duration is { } duration && DurationFormat.Positional(duration) is { } text)
            {
                Row(L.T("Duration"), text);
            }
            if (asset.CapturedAt is { } captured)
            {
                Row(L.T("Taken"), Date(captured));
            }
            Row(L.T("Modified"), Date(asset.ModifiedAt));
            if (asset.CreatedAt is { } created)
            {
                Row(L.T("Created"), Date(created));
            }
            Row(L.T("Where"), Path.GetDirectoryName(asset.Path) ?? "");
            Rows.Children.Add(_details);
            _details.Show(asset.Path, asset.Kind);
        }
        else
        {
            _details.Show(null, MediaKind.Photo);
            Rows.Children.Add(Text(assets.Count.ToString("N0", CultureInfo.CurrentCulture), "display", "JG.text.primary"));
            Row(L.T("Selected"), ByteFormat.Format(assets.Sum(a => a.Size)));
        }
    }

    private static string Date(DateTimeOffset date)
    {
        var local = date.ToLocalTime();
        return local.ToString("d", CultureInfo.CurrentCulture) + " " + local.ToString("t", CultureInfo.CurrentCulture);
    }

    private void Row(string label, string value)
    {
        var row = new StackPanel { Spacing = JGSpacing.Xxs };
        // The label style is in capitals (DESIGN.md §4).
        row.Children.Add(Text(label.ToUpper(CultureInfo.CurrentUICulture), "label", "JG.text.tertiary"));
        row.Children.Add(Text(value, "mono", "JG.text.primary", selectable: true));
        Rows.Children.Add(row);
    }

    private static TextBlock Text(string text, string style, string color, bool selectable = false)
    {
        TextBlock block = selectable ? new SelectableTextBlock() : new TextBlock();
        block.Text = text;
        block.TextWrapping = TextWrapping.Wrap;
        block.Classes.Add(style);
        block.Bind(TextElement.ForegroundProperty, block.GetResourceObservable(color));
        return block;
    }
}
