namespace JGlancer.Core.Model;

/// <summary>A keyboard movement inside the grid.</summary>
public enum GridMove
{
    Left,
    Right,
    Up,
    Down,
    PageUp,
    PageDown,
    Home,
    End,
}

/// <summary>A rectangle in grid coordinates (top-left origin, y grows downward).</summary>
public readonly record struct GridRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    public bool Intersects(GridRect other) => X <= other.Right && other.X <= Right && Y <= other.Bottom && other.Y <= Bottom;

    /// <summary>The rectangle spanned by two points, in any order.</summary>
    public static GridRect Between(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
}

/// <summary>
/// Layout math of the library grid: square tiles in rows, stretched to fill the width (macOS GridGeometry).
/// Pure and O(1) per query, so 100k items cost nothing to lay out.
/// </summary>
public sealed record GridGeometry
{
    /// <param name="targetTileSide">Preferred tile side; actual tiles stretch up to fill each row.</param>
    /// <param name="captionHeight">Space under the thumbnail for the name (0 when names are hidden).</param>
    public GridGeometry(int itemCount, double width, double targetTileSide, double gutter, double inset, double captionHeight)
    {
        ItemCount = Math.Max(0, itemCount);
        Width = width;
        Gutter = gutter;
        Inset = inset;
        CaptionHeight = captionHeight;
        var available = Math.Max(1, width - 2 * inset);
        Columns = Math.Max(1, (int)((available + gutter) / (Math.Max(1, targetTileSide) + gutter)));
        TileSide = Math.Max(1, Math.Floor((available - gutter * (Columns - 1)) / Columns));
    }

    public int ItemCount { get; }
    public double Width { get; }
    public int Columns { get; }

    /// <summary>Side of the square thumbnail area of a tile.</summary>
    public double TileSide { get; }
    public double CaptionHeight { get; }
    public double Gutter { get; }

    /// <summary>Outer padding around the grid.</summary>
    public double Inset { get; }

    /// <summary>Height of one tile including its caption.</summary>
    public double CellHeight => TileSide + CaptionHeight;

    public int RowCount => (ItemCount + Columns - 1) / Columns;

    /// <summary>Total scrollable height.</summary>
    public double ContentHeight => RowCount == 0 ? 0 : 2 * Inset + RowCount * CellHeight + (RowCount - 1) * Gutter;

    /// <summary>Frame of the tile at <paramref name="index"/>.</summary>
    public GridRect Frame(int index)
    {
        var row = index / Columns;
        var column = index % Columns;
        return new GridRect(Inset + column * (TileSide + Gutter), Inset + row * (CellHeight + Gutter), TileSide, CellHeight);
    }

    /// <summary>Indices of all tiles in the rows that intersect <paramref name="rect"/> (rendering and prefetch); start inclusive, end exclusive.</summary>
    public (int Start, int End) Indices(GridRect rect)
    {
        if (ItemCount == 0 || rect.Bottom <= Inset)
        {
            return (0, 0);
        }
        var rowPitch = CellHeight + Gutter;
        var firstRow = Math.Max(0, (int)Math.Floor((rect.Y - Inset) / rowPitch));
        var lastRow = Math.Min(RowCount - 1, (int)Math.Floor((rect.Bottom - Inset) / rowPitch));
        return firstRow > lastRow ? (0, 0) : (firstRow * Columns, Math.Min(ItemCount, (lastRow + 1) * Columns));
    }

    /// <summary>Indices whose tile actually intersects <paramref name="rect"/> (marquee selection).</summary>
    public IEnumerable<int> IndicesIntersecting(GridRect rect)
    {
        var (start, end) = Indices(rect);
        for (var index = start; index < end; index++)
        {
            if (Frame(index).Intersects(rect))
            {
                yield return index;
            }
        }
    }

    /// <summary>The tile under a point, or null for gutters and empty space.</summary>
    public int? IndexAt(double x, double y)
    {
        var (start, end) = Indices(new GridRect(x, y - 1, 0, 2));
        for (var index = start; index < end; index++)
        {
            if (Frame(index).Contains(x, y))
            {
                return index;
            }
        }
        return null;
    }

    /// <summary>Rows that fit in a viewport of <paramref name="height"/>, at least one.</summary>
    public int RowsPerPage(double height) => Math.Max(1, (int)(height / (CellHeight + Gutter)));

    /// <summary>Where a keyboard move from <paramref name="index"/> lands (clamped; left/right wrap across rows).</summary>
    public int Move(int index, GridMove move, int rowsPerPage)
    {
        if (ItemCount == 0)
        {
            return 0;
        }
        var last = ItemCount - 1;
        var target = move switch
        {
            GridMove.Left => index - 1,
            GridMove.Right => index + 1,
            GridMove.Up => index - Columns,
            GridMove.Down => index + Columns,
            GridMove.PageUp => index - Columns * rowsPerPage,
            GridMove.PageDown => index + Columns * rowsPerPage,
            GridMove.Home => 0,
            _ => last,
        };
        var isVertical = move is GridMove.Up or GridMove.Down or GridMove.PageUp or GridMove.PageDown;
        if (target < 0)
        {
            // Above the first row: stay in the same column of the first row.
            return isVertical ? index % Columns : 0;
        }
        if (target > last)
        {
            // Below the last item: jump to the last item unless already on the last row.
            return isVertical && index / Columns == last / Columns ? index : last;
        }
        return target;
    }
}
