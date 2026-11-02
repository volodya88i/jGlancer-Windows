using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using JGlancer.App.Services;
using JGlancer.Core.Actions;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Actions;

/// <summary>What the window does for the actions it owns (opening the viewer, Quick Preview).</summary>
internal interface IActionHost
{
    /// <summary>The window: owner of dialogs, the clipboard, the storage provider.</summary>
    Window Window { get; }

    void Open(long assetId);

    void ToggleQuickPreview();

    /// <summary>Moving, copying, renaming and the Recycle Bin, with undo.</summary>
    FileOps.FileOperationsModel FileOps { get; }

    /// <summary>Edits the name of a library file on its tile.</summary>
    void BeginRename(long assetId);

    /// <summary>The rename pattern dialog for several files.</summary>
    Task ShowBatchRenameAsync(ActionTarget target);

    /// <summary>A viewer's file went to the Recycle Bin: the viewer shows the next one, or closes.</summary>
    void ViewerFileRemoved();

    /// <summary>The library (albums).</summary>
    Library.LibraryModel? Library { get; }

    /// <summary>Plays songs in the window's mini player.</summary>
    void PlayInMiniPlayer(IReadOnlyList<long> assetIds);

    /// <summary>Shows two photos side by side.</summary>
    void Compare(IReadOnlyList<long> assetIds);

    /// <summary>Asks how to split the songs into stems and queues them (W-85).</summary>
    void SplitIntoStems(IReadOnlyList<string> songs);
}

/// <summary>
/// Runs the actions of the registry and names them (macOS ActionPerformer, ARCHITECTURE §15); the context menus and
/// the menu bar both come here. Open With and the file facts (W6), moving, copying, renaming and the Recycle Bin
/// (W7), albums (W8), flags (W10).
/// </summary>
internal sealed class ActionPerformer(AppServices services, IActionHost host)
{
    private InfoWindow? _info;

    /// <summary>The registry's features built so far.</summary>
    public static IReadOnlySet<ActionFeature> Features { get; } = new HashSet<ActionFeature> { ActionFeature.OpenWith, ActionFeature.FileOperations, ActionFeature.Albums, ActionFeature.Flags, ActionFeature.MiniPlayer, ActionFeature.StemSplitting };

    public OpenWithStore OpenWith { get; } = new(services.OpenWith);

    /// <summary>The visible actions for <paramref name="target"/>, by section.</summary>
    public IReadOnlyList<IReadOnlyList<ActionMenuItem>> Menu(ActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ActionRegistry.Menu(target.Selection, FeaturesFor(target), OpenWith.Settings.KindsWithPrimary);
    }

    public bool IsAvailable(ActionId id, ActionTarget? target) =>
        target is not null && ActionRegistry.Availability(id, target.Selection, FeaturesFor(target), OpenWith.Settings.KindsWithPrimary) == ActionAvailability.Enabled;

    private static readonly IReadOnlySet<ActionFeature> OutsideFeatures = Features.Where(f => f is not (ActionFeature.Flags or ActionFeature.Albums or ActionFeature.MiniPlayer)).ToHashSet();

    /// <summary>Files outside the library (stand-ins) have no flags or albums: those live in the library (macOS D-64).</summary>
    private static IReadOnlySet<ActionFeature> FeaturesFor(ActionTarget target) =>
        target.AssetIds.Any(Services.StandIns.IsStandIn) ? OutsideFeatures : Features;

    public string Title(ActionId id, ActionTarget? target) => id switch
    {
        ActionId.Open => L.T("Open"),
        ActionId.QuickPreview => L.T("Quick Preview"),
        ActionId.PlayInMiniPlayer => L.T("Play in Mini Player"),
        ActionId.Compare => L.T("Compare"),
        ActionId.SplitIntoStems => L.T("Split into Stems…"),
        ActionId.OpenWith => L.T("Open With"),
        ActionId.EditInPrimaryApp => target?.Kind is { } kind && OpenWith.Settings.Primary(kind) is { } app
            ? L.T("Edit in {0}", app.Name)
            : L.T("Edit in Primary App"),
        ActionId.GetInfo => L.T("Get Info"),
        ActionId.ShowInExplorer => L.T("Show in Explorer"),
        ActionId.CopyPath => L.T("Copy Path"),
        ActionId.Copy => L.T("Copy"),
        ActionId.MoveTo => L.T("Move to…"),
        ActionId.CopyTo => L.T("Copy to…"),
        ActionId.Rename => L.T("Rename…"),
        ActionId.MoveToTrash => L.T("Move to Recycle Bin"),
        ActionId.AddToAlbum => L.T("Add to Album"),
        ActionId.RemoveFromAlbum => L.T("Remove from Album"),
        ActionId.SetAlbumCover => L.T("Use as Album Cover"),
        ActionId.Flag => L.T("Flag"),
        _ => id.ToString(),
    };

    public async Task PerformAsync(ActionId id, ActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsAvailable(id, target))
        {
            return;
        }
        switch (id)
        {
            case ActionId.Open when target.AssetIds.Count > 0:
                host.Open(target.AssetIds[0]);
                break;
            case ActionId.QuickPreview:
                host.ToggleQuickPreview();
                break;
            case ActionId.PlayInMiniPlayer:
                host.PlayInMiniPlayer(target.AssetIds);
                break;
            case ActionId.SplitIntoStems:
                host.SplitIntoStems(await target.Paths());
                break;
            case ActionId.Compare when target.AssetIds.Count == 2:
                host.Compare(target.AssetIds);
                break;
            case ActionId.EditInPrimaryApp when target.Kind is { } kind && OpenWith.Settings.Primary(kind) is { } app:
                ShellOpenWith.OpenWith(app.Path, await target.Paths());
                break;
            case ActionId.GetInfo:
                await ShowInfoAsync(target);
                break;
            case ActionId.ShowInExplorer:
                ShellFiles.ShowInExplorer(await target.Paths());
                break;
            case ActionId.CopyPath when host.Window.Clipboard is { } clipboard:
                await clipboard.SetTextAsync(string.Join(Environment.NewLine, await target.Paths()));
                break;
            case ActionId.Copy:
                await CopyFilesAsync(target);
                break;
            case ActionId.MoveTo or ActionId.CopyTo:
                await TransferAsync(target, null, id == ActionId.CopyTo);
                break;
            case ActionId.Rename when target.Count > 1:
                await host.ShowBatchRenameAsync(target);
                break;
            case ActionId.Rename when target.Place != ActionPlace.Viewer:
                host.BeginRename(target.AssetIds[0]);
                break;
            case ActionId.RemoveFromAlbum when host.Library is { } library:
                await library.RemoveFromCurrentAlbumAsync(target.AssetIds);
                break;
            case ActionId.SetAlbumCover when host.Library is { } library:
                await library.SetCoverAsync(target.AssetIds[0]);
                break;
            case ActionId.MoveToTrash:
                var trashed = await host.FileOps.TrashAsync(await target.Paths());
                if (target.Place == ActionPlace.Viewer && trashed is { Completed: > 0 })
                {
                    host.ViewerFileRemoved();
                }
                break;
        }
    }

    /// <summary>Flag ▸ and the flag keys: the files get <paramref name="flag"/>, or lose it if they all had it (undoable).</summary>
    public async Task SetFlagAsync(ActionTarget target, AssetFlag flag, bool continuesChord = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (host.Library is { } library)
        {
            await library.SetFlagAsync(flag, target.AssetIds, continuesChord);
        }
    }

    /// <summary>Albums for the Add to Album submenu (the one shown is left out), with how many of the files each already holds.</summary>
    public IReadOnlyList<(Core.Model.Album Album, int Contained)> AlbumsForAdding(ActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (host.Library is not { } library)
        {
            return [];
        }
        return [.. library.Albums.Where(a => a.Id != library.State.AlbumId).Select(album =>
            (album, target.AssetIds.Count(id => target.Memberships?.GetValueOrDefault(id)?.Contains(album.Id) == true)))];
    }

    public async Task AddToAlbumAsync(ActionTarget target, long albumId)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (host.Library is { } library)
        {
            await library.AddToAlbumAsync(albumId, target.AssetIds);
        }
    }

    /// <summary>New Album… from the files: the album is made with them and named in the sidebar.</summary>
    public async Task AddToNewAlbumAsync(ActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (host.Library is { } library)
        {
            await library.CreateAlbumAsync(target.AssetIds);
        }
    }

    /// <summary>Recent destinations of Move to… and Copy to….</summary>
    public IReadOnlyList<string> RecentFolders => host.FileOps.RecentFolders;

    /// <summary>Move to… / Copy to… into <paramref name="folder"/> (null: choose one).</summary>
    public async Task TransferAsync(ActionTarget target, string? folder, bool copying)
    {
        ArgumentNullException.ThrowIfNull(target);
        await host.FileOps.TransferAsync(await target.Paths(), folder, copying);
    }

    /// <summary>Opens the files with an Open With entry.</summary>
    public async Task OpenAsync(ActionTarget target, OpenWithEntry entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        var paths = await target.Paths();
        if (paths.Count == 0)
        {
            return;
        }
        switch (entry)
        {
            case OpenWithEntry.Configured { IsInstalled: true } configured:
                ShellOpenWith.OpenWith(configured.App.Path, paths);
                break;
            case OpenWithEntry.Suggested suggested:
                suggested.Handler.Open(paths);
                break;
            case OpenWithEntry.Other:
                await OpenWithOtherAsync(paths);
                break;
        }
    }

    /// <summary>
    /// One file: Windows' own "Open with" dialog (as Explorer's "Choose another app"). Several: any program, chosen
    /// in a file dialog (the Windows dialog takes one file).
    /// </summary>
    private async Task OpenWithOtherAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
        {
            ShellOpenWith.ShowDialog(host.Window.TryGetPlatformHandle()?.Handle ?? 0, paths[0]);
            return;
        }
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var chosen = await host.Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Other…"),
            AllowMultiple = false,
            SuggestedStartLocation = await host.Window.StorageProvider.TryGetFolderFromPathAsync(new Uri(programs + Path.DirectorySeparatorChar)),
            FileTypeFilter = [new FilePickerFileType(L.T("Programs")) { Patterns = ["*.exe"] }],
        });
        if (chosen.Count > 0 && chosen[0].TryGetLocalPath() is { } program)
        {
            ShellOpenWith.OpenWith(program, paths);
        }
    }

    private async Task ShowInfoAsync(ActionTarget target)
    {
        var assets = await services.AssetsAsync(target.AssetIds);
        if (assets.Count == 0)
        {
            return;
        }
        // Rows come back by id: show them in the target's order.
        var order = target.AssetIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        var sorted = assets.OrderBy(a => order.GetValueOrDefault(a.Id)).ToList();
        if (_info is null)
        {
            _info = new InfoWindow();
            _info.Closed += (_, _) => _info = null;
            _info.Fill(sorted);
            _info.Show(host.Window);
        }
        else
        {
            _info.Fill(sorted);
            _info.Activate();
        }
    }

    /// <summary>The files on the clipboard (paste in Explorer copies them).</summary>
    private async Task CopyFilesAsync(ActionTarget target)
    {
        if (host.Window.Clipboard is not { } clipboard)
        {
            return;
        }
        var items = new List<IStorageItem>();
        foreach (var path in await target.Paths())
        {
            if (await host.Window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)) is { } file)
            {
                items.Add(file);
            }
        }
        if (items.Count > 0)
        {
            await clipboard.SetFilesAsync(items);
        }
    }
}
