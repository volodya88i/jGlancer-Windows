using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

public sealed class GridGeometryTests
{
    // 1000 wide, 160 target, 8 gutter, 16 inset: (968 + 8) / 168 = 5 columns of 187.
    private readonly GridGeometry _grid = new(23, 1000, 160, 8, 16, 20);

    [Fact]
    public void ColumnsStretchToFillTheRow()
    {
        Assert.Equal(5, _grid.Columns);
        Assert.Equal(187, _grid.TileSide);
        Assert.Equal(5, _grid.RowCount);
        Assert.Equal(new GridRect(16 + 195, 16 + 215, 187, 207), _grid.Frame(6));
        Assert.Equal(32 + 5 * 207 + 4 * 8, _grid.ContentHeight);
    }

    [Fact]
    public void NarrowWindowsKeepOneColumn()
    {
        var narrow = new GridGeometry(3, 50, 160, 8, 16, 0);
        Assert.Equal(1, narrow.Columns);
        Assert.Equal(18, narrow.TileSide);
    }

    [Fact]
    public void VisibleRangeCoversWholeRows()
    {
        Assert.Equal((0, 5), _grid.Indices(new GridRect(0, 0, 1000, 100)));
        Assert.Equal((5, 15), _grid.Indices(new GridRect(0, 240, 1000, 300)));
        Assert.Equal((0, 0), _grid.Indices(new GridRect(0, 5000, 1000, 300)));
        Assert.Equal((0, 0), new GridGeometry(0, 1000, 160, 8, 16, 0).Indices(new GridRect(0, 0, 1000, 1000)));
    }

    [Fact]
    public void HitTestingIgnoresGutters()
    {
        Assert.Equal(0, _grid.IndexAt(20, 20));
        Assert.Null(_grid.IndexAt(16 + 187 + 4, 20));
        Assert.Equal(6, _grid.IndexAt(16 + 195 + 1, 16 + 215 + 1));
        Assert.Null(_grid.IndexAt(900, 16 + 4 * 215 + 10)); // past the last item
    }

    [Fact]
    public void MarqueeSelectsIntersectingTilesOnly() =>
        Assert.Equal([0, 1, 5, 6], _grid.IndicesIntersecting(new GridRect(100, 100, 150, 150)));

    [Fact]
    public void KeyboardMovesAreClamped()
    {
        Assert.Equal(5, _grid.Move(6, GridMove.Left, 2));
        Assert.Equal(5, _grid.Move(4, GridMove.Right, 2));
        Assert.Equal(1, _grid.Move(6, GridMove.Up, 2));
        Assert.Equal(2, _grid.Move(2, GridMove.Up, 2));
        Assert.Equal(22, _grid.Move(17, GridMove.Down, 2));
        Assert.Equal(21, _grid.Move(21, GridMove.Down, 2));
        Assert.Equal(11, _grid.Move(1, GridMove.PageDown, 2));
        Assert.Equal(2, _grid.Move(7, GridMove.PageUp, 2));
        Assert.Equal(0, _grid.Move(7, GridMove.Home, 2));
        Assert.Equal(22, _grid.Move(7, GridMove.End, 2));
    }
}

public sealed class SelectionModelTests
{
    private readonly OrderedIds _order = new([10, 11, 12, 13, 14, 15]);

    [Fact]
    public void ClickToggleAndShiftRange()
    {
        var selection = new SelectionModel();
        selection.Select(11);
        selection.Extend(13, _order);
        Assert.Equal([11, 12, 13], selection.Selected.Order());
        selection.Extend(10, _order);
        Assert.Equal([10, 11], selection.Selected.Order());
        selection.Toggle(15);
        Assert.Equal([10, 11, 15], selection.Selected.Order());
        selection.Extend(13, _order);
        Assert.Equal([10, 11, 13, 14, 15], selection.Selected.Order());
        selection.Toggle(10);
        Assert.Equal([11, 13, 14, 15], selection.Selected.Order());
        Assert.Equal(10, selection.Focus);
    }

    [Fact]
    public void MarqueeReplacesOrAdds()
    {
        var selection = new SelectionModel();
        selection.Select(10);
        selection.BeginMarquee(adding: true);
        selection.Marquee([12, 13]);
        Assert.Equal([10, 12, 13], selection.Selected.Order());
        selection.Marquee([13]);
        Assert.Equal([10, 13], selection.Selected.Order());
        selection.CommitMarquee(13);
        selection.BeginMarquee(adding: false);
        Assert.True(selection.IsEmpty);
        selection.Marquee([15]);
        Assert.Equal([15], selection.Selected);
    }

    [Fact]
    public void SelectAllRetainAndChangeEvents()
    {
        var selection = new SelectionModel();
        var changes = 0;
        selection.Changed += (_, _) => changes++;
        selection.SelectAll(_order);
        Assert.Equal(6, selection.Count);
        selection.Retain(new OrderedIds([12, 99]));
        Assert.Equal([12], selection.Selected);
        Assert.Null(selection.Focus);
        selection.Retain(new OrderedIds([12]));
        Assert.Equal(2, changes);
    }
}

public sealed class DurationAndViewStateTests
{
    [Theory]
    [InlineData(0.0, "0:00")]
    [InlineData(2.4, "0:02")]
    [InlineData(59.6, "1:00")]
    [InlineData(125, "2:05")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    public void PositionalTimeCodes(double seconds, string expected) => Assert.Equal(expected, DurationFormat.Positional(seconds));

    [Fact]
    public void TenthsAndInvalidInput()
    {
        Assert.Equal("1:02.3", DurationFormat.Tenths(62.34));
        Assert.Equal("1:00.0", DurationFormat.Tenths(59.97));
        Assert.Null(DurationFormat.Tenths(-1));
        Assert.Null(DurationFormat.Positional(double.NaN));
        Assert.Null(DurationFormat.Positional(double.PositiveInfinity));
    }

    [Fact]
    public void ViewStateRoundTripsAndToleratesMissingOrBadKeys()
    {
        var state = new LibraryViewState
        {
            FolderId = 7, Kinds = [MediaKind.Audio], Sort = AssetSort.DateCaptured, SelectedIds = [1, 2],
            PreviewVisible = false, PreviewWidth = 420, PinnedFolders = [9, 4],
        };
        var decoded = LibraryViewState.FromJson(state.ToJson());
        Assert.Equal(state.FolderId, decoded.FolderId);
        Assert.Equal(state.Kinds, decoded.Kinds);
        Assert.Equal(state.Sort, decoded.Sort);
        Assert.Equal(state.SelectedIds, decoded.SelectedIds);
        Assert.Equal((false, 420.0), (decoded.PreviewVisible, decoded.PreviewWidth));
        Assert.Equal([9L, 4], decoded.PinnedFolders);
        // An empty kind list means all kinds; unreadable JSON gives the defaults.
        Assert.Equal(3, LibraryViewState.FromJson("""{"kinds":[]}""").KindSet.Count);
        Assert.Equal(AssetSort.Name, LibraryViewState.FromJson("""{"sort":"bogus"}""").Sort);
        var older = LibraryViewState.FromJson("""{"folderId":3,"tileSide":200}""");
        Assert.Equal((3L, 200.0, true), (older.FolderId!.Value, older.TileSide, older.PreviewVisible));
        Assert.Empty(older.PinnedFolders);
    }
}
