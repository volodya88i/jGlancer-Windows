using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library.List;

/// <summary>
/// Drawing the list (D-125): the header pinned under the top bar, stripes as in Explorer and Finder (<c>bg.panel</c>
/// every other row, also below the last), the selection as <c>accent.dim</c> in a rounded row, a disclosure chevron for
/// folders, a small thumbnail or glyph, the name and its flag, then the columns in <c>text.secondary</c>.
/// </summary>
internal sealed partial class LibraryList
{
    private const double Indent = 16;
    private const double Disclosure = 16;
    private const double IconGap = 8;
    private static readonly Typeface TextFace = new(JGFonts.Text);
    private static readonly Typeface HeaderFace = new(JGFonts.Text, FontStyle.Normal, FontWeight.SemiBold);

    private readonly Dictionary<(string Text, double Width, int Style, IBrush Brush), FormattedText> _texts = [];
    private static readonly Dictionary<string, Geometry> Paths = [];
    private Avalonia.Styling.ThemeVariant? _textTheme;

    /// <summary>The place of every column: Name first, then the saved ones; Name takes what is left.</summary>
    private List<(ListColumn? Column, double X, double Width)> Columns()
    {
        var saved = _model?.State.ListColumns ?? ListLayout.DefaultColumns;
        var padding = JGSpacing.S;
        var fixedWidth = saved.Sum(c => c.Width);
        var nameWidth = Math.Max(JGMetrics.ListNameMinWidth, Bounds.Width - 2 * padding - fixedWidth);
        var columns = new List<(ListColumn? Column, double X, double Width)> { (null, padding, nameWidth) };
        var x = padding + nameWidth;
        foreach (var column in saved)
        {
            columns.Add((column.Column, x, column.Width));
            x += column.Width;
        }
        return columns;
    }

    private double IconSide => Math.Clamp(RowHeight - 6, 14, 64);

    /// <summary>Where the name's text starts in a row of <paramref name="depth"/>, and how wide it may be.</summary>
    private (double X, double Width) NameTextSpan(int depth)
    {
        var name = Columns()[0];
        var x = name.X + depth * Indent + Disclosure + IconSide + IconGap;
        return (x, Math.Max(0, name.X + name.Width - x - JGSpacing.Xs));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_model is null || _visible.Count == 0)
        {
            // Nothing to list: the pane's empty state shows instead (no stripes or header over it).
            return;
        }
        if (_textTheme != ActualThemeVariant)
        {
            _textTheme = ActualThemeVariant;
            _texts.Clear();
        }
        var columns = Columns();
        var height = RowHeight;
        var panel = this.Brush(JGColor.BgPanel);
        // Stripes down to the bottom, the selection over them.
        var first = Math.Max(0, (int)Math.Floor((_offset.Y - RowsTop) / height));
        for (var index = first; RowsTop + index * height - _offset.Y < Bounds.Height; index++)
        {
            var y = RowsTop + index * height - _offset.Y;
            if (index % 2 == 1)
            {
                context.FillRectangle(panel, new Rect(0, y, Bounds.Width, height));
            }
        }
        var (start, end) = VisibleRange();
        for (var index = start; index < end; index++)
        {
            DrawRow(context, columns, _visible[index], RowsTop + index * height - _offset.Y, height);
        }
        DrawHeader(context, columns);
    }

    private void DrawRow(DrawingContext context, List<(ListColumn? Column, double X, double Width)> columns, VisibleListRow item, double y, double height)
    {
        var row = item.Row;
        var asset = row.IsFolder ? null : _rows.GetValueOrDefault(row.Id);
        var selected = row.IsFolder ? FolderFocus == row.Id : _model!.Selection.Contains(row.Id);
        if (selected)
        {
            var inset = JGSpacing.Xxs;
            context.DrawRectangle(this.Brush(JGColor.AccentDim), null,
                new Rect(inset, y + 1, Bounds.Width - 2 * inset, height - 2), JGMetrics.RadiusSmall, JGMetrics.RadiusSmall);
        }
        var name = columns[0];
        var x = name.X + item.Depth * Indent;
        var middle = y + height / 2;
        var secondary = this.Brush(JGColor.TextSecondary);
        if (row.IsFolder)
        {
            DrawPath(context, item.IsExpanded ? JGIconData.ChevronDown : JGIconData.ChevronRight,
                new Rect(x + 2, middle - 6, 12, 12), secondary, 2);
        }
        var icon = new Rect(x + Disclosure, middle - IconSide / 2, IconSide, IconSide);
        DrawIcon(context, row, asset, icon);
        var (textX, textWidth) = NameTextSpan(item.Depth);
        var label = row.IsFolder ? _outline.Folders.GetValueOrDefault(row.Id)?.Name ?? "" : asset?.Name ?? "";
        var primary = this.Brush(JGColor.TextPrimary);
        var flag = asset?.Flag ?? AssetFlag.None;
        var star = flag == AssetFlag.None ? 0 : JGMetrics.FlagStarSize + JGSpacing.Xs;
        var text = Text(label, Math.Max(0, textWidth - star), 0, primary);
        context.DrawText(text, new Point(textX, middle - text.Height / 2));
        if (flag != AssetFlag.None)
        {
            var size = JGMetrics.FlagStarSize;
            FlagStyle.Draw(context, this.Brush(FlagStyle.Token(flag)),
                new Rect(textX + text.WidthIncludingTrailingWhitespace + JGSpacing.Xs, middle - size / 2, size, size));
        }
        foreach (var (column, columnX, width) in columns.Skip(1))
        {
            var value = column switch
            {
                ListColumn.DateModified => asset?.ModifiedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "",
                ListColumn.Size => row.IsFolder ? "—" : asset is null ? "" : ByteFormat.Format(asset.Size),
                _ => KindText(row, asset),
            };
            var cell = Text(value, Math.Max(0, width - 2 * JGSpacing.Xs), 1, secondary);
            var cellX = column == ListColumn.Size ? columnX + width - JGSpacing.Xs - cell.Width : columnX + JGSpacing.Xs;
            context.DrawText(cell, new Point(cellX, middle - cell.Height / 2));
        }
    }

    /// <summary>"Folder", "Image sequence (N)" for a folded sequence, or the name Explorer gives the file type.</summary>
    private string KindText(ListRow row, Asset? asset)
    {
        if (row.IsFolder)
        {
            return L.T("Folder");
        }
        if (_model!.IsFolded(row.Id) && _model.SequenceOf(row.Id) is { } sequence)
        {
            return L.T("Image sequence ({0})", sequence.Frames.Count);
        }
        if (asset is null)
        {
            return "";
        }
        var extension = Path.GetExtension(asset.Name);
        return Platform.Windows.ShellTypeNames.Of(extension) ?? extension.TrimStart('.').ToUpperInvariant();
    }

    /// <summary>A small thumbnail, or a glyph: folder, kind, or cloud for a file kept only there.</summary>
    private void DrawIcon(DrawingContext context, ListRow row, Asset? asset, Rect box)
    {
        if (asset is not null && Thumbnails.TryGet(ThumbnailRequest.For(asset, ThumbnailBucket.Small).Key, out var bitmap) && bitmap is not null)
        {
            var fit = Grid.LibraryGrid.AspectFit(bitmap.Size, box);
            using (context.PushClip(new RoundedRect(fit, JGMetrics.BadgeRadius)))
            {
                context.DrawImage(bitmap, new Rect(bitmap.Size), fit);
            }
            return;
        }
        var glyph = row.IsFolder ? JGIconData.Folder
            : asset?.IsCloudOnly == true ? JGCustomIcons.Cloud
            : asset?.Kind switch
            {
                MediaKind.Video => JGIconData.Film,
                MediaKind.Audio => JGIconData.AudioLines,
                _ => JGIconData.Image,
            };
        var side = Math.Min(box.Height, JGMetrics.IconSize);
        DrawPath(context, glyph, new Rect(box.Center.X - side / 2, box.Center.Y - side / 2, side, side),
            this.Brush(row.IsFolder ? JGColor.TextSecondary : JGColor.TextTertiary), 1.75);
    }

    private static void DrawPath(DrawingContext context, string data, Rect box, IBrush brush, double stroke)
    {
        var scale = box.Width / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X, box.Y)))
        {
            if (!Paths.TryGetValue(data, out var geometry))
            {
                Paths[data] = geometry = Geometry.Parse(data);
            }
            context.DrawGeometry(null, new Pen(brush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }
    }

    private void DrawHeader(DrawingContext context, List<(ListColumn? Column, double X, double Width)> columns)
    {
        var top = _insets.Top;
        var rect = new Rect(0, top, Bounds.Width, HeaderHeight);
        context.FillRectangle(this.Brush(JGColor.BgBase), rect);
        var hair = new Pen(this.Brush(JGColor.LineHair), JGMetrics.OutlineWidth);
        context.DrawLine(hair, new Point(0, rect.Bottom - 0.5), new Point(Bounds.Width, rect.Bottom - 0.5));
        var state = _model!.State;
        var sorted = SortColumn(state.Sort);
        var secondary = this.Brush(JGColor.TextSecondary);
        for (var i = 0; i < columns.Count; i++)
        {
            var (column, x, width) = columns[i];
            var isSorted = state.Sort == AssetSort.Name ? column is null : column == sorted && sorted is not null;
            var title = Text(Title(column), Math.Max(0, width - 2 * JGSpacing.Xs - (isSorted ? 14 : 0)), 2,
                isSorted ? this.Brush(JGColor.TextPrimary) : secondary);
            var textX = x + JGSpacing.Xs;
            context.DrawText(title, new Point(textX, top + (HeaderHeight - title.Height) / 2));
            if (isSorted)
            {
                DrawPath(context, state.Ascending ? JGIconData.ChevronUp : JGIconData.ChevronDown,
                    new Rect(textX + title.Width + 4, top + HeaderHeight / 2 - 5, 10, 10), secondary, 2);
            }
            if (i > 0)
            {
                context.DrawLine(hair, new Point(x + 0.5, top + 6), new Point(x + 0.5, rect.Bottom - 6));
            }
        }
        if (_movingColumn is { } moving)
        {
            // The column being moved, under the pointer.
            context.DrawRectangle(new SolidColorBrush(Colors.Transparent), new Pen(this.Brush(JGColor.Accent), JGMetrics.OutlineWidth),
                new Rect(moving.X, top, moving.Width, HeaderHeight));
        }
    }

    private static string Title(ListColumn? column) => column switch
    {
        null => L.T("Name"),
        ListColumn.DateModified => L.T("Date Modified"),
        ListColumn.Size => L.T("Size"),
        _ => L.T("Kind"),
    };

    private static ListColumn? SortColumn(AssetSort sort) => sort switch
    {
        AssetSort.DateModified => ListColumn.DateModified,
        AssetSort.Size => ListColumn.Size,
        AssetSort.Kind => ListColumn.Kind,
        _ => null,
    };

    /// <summary>Body text trimmed to <paramref name="width"/> with an ellipsis; style 0 name, 1 cell, 2 header.</summary>
    private FormattedText Text(string text, double width, int style, IBrush brush)
    {
        var key = (text, Math.Round(width), style, brush);
        if (_texts.TryGetValue(key, out var cached))
        {
            return cached;
        }
        if (_texts.Count > 6000)
        {
            _texts.Clear();
        }
        var size = style == 2 ? JGTypeScale.Label.Size : JGTypeScale.Body.Size;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, style == 2 ? HeaderFace : TextFace, size, brush)
        {
            MaxTextWidth = Math.Max(1, width),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        _texts[key] = formatted;
        return formatted;
    }
}
