using Avalonia.Controls;
using Avalonia.Platform.Storage;
using JGlancer.Core.Localization;

namespace JGlancer.App.Support;

/// <summary>The system folder picker, several folders at once.</summary>
internal static class FolderPicker
{
    public static async Task<IReadOnlyList<string>> ChooseAsync(TopLevel top)
    {
        ArgumentNullException.ThrowIfNull(top);
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = L.T("Add Folder…"),
        });
        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }
}
