using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The flag star of a library file beside its name (viewers, the preview pane; macOS FlagStar): nothing when the file
/// is unflagged or outside the library. Follows flag changes while it is shown.
/// </summary>
internal sealed class FlagStar : ContentControl
{
    /// <summary>The library whose flags the stars show (set once by the main window, macOS FlagBook).</summary>
    public static LibraryModel? Library { get; set; }

    private long? _assetId;
    private int _version;

    public FlagStar()
    {
        IsVisible = false;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <summary>The file shown; null or 0 for none.</summary>
    public long? AssetId
    {
        get => _assetId;
        set
        {
            if (value != _assetId)
            {
                _assetId = value;
                _ = RefreshAsync();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Library is { } library)
        {
            library.FlagsChanged += OnFlagsChanged;
        }
        _ = RefreshAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Library is { } library)
        {
            library.FlagsChanged -= OnFlagsChanged;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFlagsChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++_version;
        var flag = _assetId is > 0 and var id && Library is { } library ? await library.FlagOfAsync(id) : AssetFlag.None;
        if (version != _version)
        {
            return;
        }
        IsVisible = flag != AssetFlag.None;
        Content = flag == AssetFlag.None ? null : FlagStyle.Glyph(this, flag, JGMetrics.FlagStarSize);
        AutomationProperties.SetName(this, flag == AssetFlag.None ? null : L.T("Flag: {0}", FlagStyle.Title(flag)));
    }
}
