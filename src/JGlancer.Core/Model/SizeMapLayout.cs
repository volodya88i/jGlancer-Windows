namespace JGlancer.Core.Model;

/// <summary>A rectangle of the size map, in effective pixels; y grows downward.</summary>
public readonly record struct MapRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Smaller by <paramref name="dx"/> on the left and right, <paramref name="dy"/> on the top and bottom.</summary>
    public MapRect Inset(double dx, double dy) => new(X + dx, Y + dy, Math.Max(0, Width - 2 * dx), Math.Max(0, Height - 2 * dy));
}

/// <summary>
/// Where every node of a <see cref="SizeTree"/> lies in a rectangle (macOS D-127): squarified rectangles (Bruls,
/// Huizing, van Wijk), each folder with a band for its name at the top when there is room and a thin padding round its
/// contents. Rectangles smaller than the minimum side get no children laid out inside them.
/// </summary>
public sealed class SizeMapLayout
{
    /// <summary>A laid-out node, in drawing order (parents before their children).</summary>
    /// <param name="Header">The folder's name band, when it has one.</param>
    public readonly record struct Box(int Node, MapRect Rect, MapRect? Header, int Depth);

    /// <summary>The layout of nothing.</summary>
    public static SizeMapLayout Empty { get; } = new([], []);

    private SizeMapLayout(IReadOnlyList<Box> boxes, IReadOnlyList<MapRect?> rects)
    {
        Boxes = boxes;
        Rects = rects;
    }

    /// <summary>The boxes in drawing order.</summary>
    public IReadOnlyList<Box> Boxes { get; }

    /// <summary>The rectangle of each node, by its index; null when it was too small to lay out.</summary>
    public IReadOnlyList<MapRect?> Rects { get; }

    /// <summary>Lays <paramref name="tree"/> out in <paramref name="bounds"/>.</summary>
    /// <param name="header">A folder's name band height (it gets one when at least twice as high and three times as wide).</param>
    /// <param name="padding">Room inside a folder round its contents.</param>
    /// <param name="minSide">Nothing is laid out inside a folder whose inner rectangle is smaller.</param>
    public static SizeMapLayout Make(SizeTree tree, MapRect bounds, double header, double padding, double minSide)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var boxes = new List<Box>();
        var rects = new MapRect?[tree.Nodes.Count];
        void Place(int index, MapRect rect, int depth)
        {
            if (rect.Width < 1 || rect.Height < 1)
            {
                return;
            }
            rects[index] = rect;
            var node = tree.Nodes[index];
            if (node.Children.Count == 0)
            {
                boxes.Add(new Box(index, rect, null, depth));
                return;
            }
            var hasHeader = rect.Height >= header * 2 && rect.Width >= header * 3;
            MapRect? band = hasHeader ? new MapRect(rect.X, rect.Y, rect.Width, header) : null;
            boxes.Add(new Box(index, rect, band, depth));
            var top = hasHeader ? header : 0;
            var inner = new MapRect(rect.X, rect.Y + top, rect.Width, rect.Height - top).Inset(padding, padding);
            if (inner.Width < minSide || inner.Height < minSide)
            {
                return;
            }
            var parts = Squarify([.. node.Children.Select(child => (double)tree.Nodes[child].Size)], inner);
            for (var i = 0; i < parts.Count; i++)
            {
                Place(node.Children[i], parts[i], depth + 1);
            }
        }
        if (!tree.IsEmpty)
        {
            Place(0, bounds, 0);
        }
        return new SizeMapLayout(boxes, rects);
    }

    /// <summary>The deepest node whose rectangle holds the point; null outside the map.</summary>
    public int? Hit(double x, double y, SizeTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.IsEmpty || Rects.Count == 0 || Rects[0] is not { } root || !root.Contains(x, y))
        {
            return null;
        }
        var current = 0;
        while (tree.Nodes[current].Children.FirstOrDefault(child => Rects[child]?.Contains(x, y) == true, -1) is var next and >= 0)
        {
            current = next;
        }
        return current;
    }

    /// <summary>Rectangles for <paramref name="values"/> (largest first works best) filling <paramref name="rect"/>, each with an area in proportion to its value.</summary>
    public static IReadOnlyList<MapRect> Squarify(IReadOnlyList<double> values, MapRect rect)
    {
        ArgumentNullException.ThrowIfNull(values);
        var total = values.Sum();
        var result = new MapRect[values.Count];
        if (total <= 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return result;
        }
        var scale = rect.Width * rect.Height / total;
        var areas = values.Select(value => value * scale).ToArray();
        var remaining = rect;
        var start = 0;
        while (start < areas.Length)
        {
            var side = Math.Min(remaining.Width, remaining.Height);
            var end = start + 1;
            var rowArea = areas[start];
            var worst = Worst(areas, start, end, rowArea, side);
            while (end < areas.Length)
            {
                var candidate = Worst(areas, start, end + 1, rowArea + areas[end], side);
                if (candidate > worst)
                {
                    break;
                }
                worst = candidate;
                rowArea += areas[end];
                end++;
            }
            remaining = PlaceRow(start, end, areas, rowArea, remaining, result);
            start = end;
        }
        return result;
    }

    /// <summary>Lays the row along the shorter side of <paramref name="remaining"/> (a column at the left, or a row at the top) and returns the room left.</summary>
    private static MapRect PlaceRow(int start, int end, double[] areas, double rowArea, MapRect remaining, MapRect[] result)
    {
        if (remaining.Width >= remaining.Height)
        {
            var width = remaining.Height > 0 ? rowArea / remaining.Height : 0;
            var y = remaining.Y;
            for (var i = start; i < end; i++)
            {
                var height = width > 0 ? areas[i] / width : 0;
                result[i] = new MapRect(remaining.X, y, width, height);
                y += height;
            }
            return new MapRect(remaining.X + width, remaining.Y, Math.Max(remaining.Width - width, 0), remaining.Height);
        }
        var rowHeight = remaining.Width > 0 ? rowArea / remaining.Width : 0;
        var x = remaining.X;
        for (var i = start; i < end; i++)
        {
            var width = rowHeight > 0 ? areas[i] / rowHeight : 0;
            result[i] = new MapRect(x, remaining.Y, width, rowHeight);
            x += width;
        }
        return new MapRect(remaining.X, remaining.Y + rowHeight, remaining.Width, Math.Max(remaining.Height - rowHeight, 0));
    }

    /// <summary>The worst aspect ratio of the row <paramref name="start"/>..<paramref name="end"/> laid along <paramref name="side"/>.</summary>
    private static double Worst(double[] areas, int start, int end, double sum, double side)
    {
        var largest = double.MinValue;
        var smallest = double.MaxValue;
        for (var i = start; i < end; i++)
        {
            largest = Math.Max(largest, areas[i]);
            smallest = Math.Min(smallest, areas[i]);
        }
        if (sum <= 0 || side <= 0 || smallest <= 0)
        {
            return double.PositiveInfinity;
        }
        var squared = side * side;
        return Math.Max(squared * largest / (sum * sum), sum * sum / (squared * smallest));
    }
}
