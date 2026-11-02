using Avalonia.Input;
using JGlancer.App.Library;

namespace JGlancer.App.Shell;

/// <summary>
/// Files dropped on the window from Explorer open (W-89), as if opened from Explorer: a library file
/// with its folder or album, any other file with the media files of its folder around it. Folders dropped on a pane
/// still join the library; the app's own tiles dragged out and back are not opened.
/// </summary>
internal sealed partial class MainWindow
{
    private void AttachFileDrop()
    {
        Library.FilesDropped += async (_, files) => await OpenPathsAsync(files);
        // Over a viewer (the panes are hidden then): the same.
        DragDrop.SetAllowDrop(Body, true);
        Body.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (!e.Handled && BrowserPane.DroppedMediaFiles(e).Count > 0)
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        });
        Body.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (!e.Handled && BrowserPane.DroppedMediaFiles(e) is { Count: > 0 } files)
            {
                e.Handled = true;
                await OpenPathsAsync(files);
            }
        });
    }
}
