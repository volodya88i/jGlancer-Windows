using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using JGlancer.App.Library;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Preview;

/// <summary>The preview pane (macOS PreviewPane): hidden by its × or View ▸ Show Preview, moved by its header (D-127).</summary>
internal sealed partial class PreviewPane : UserControl
{
    /// <summary>Side of the backdrop's tiny copy of the cover (as the audio viewer).</summary>
    private const int BackdropPixels = 8;

    private PreviewModel? _model;
    private LibraryModel? _library;
    private Avalonia.Media.Imaging.Bitmap? _backdropFor;

    public PreviewPane()
    {
        InitializeComponent();
        // Settings → Appearance: the blurred cover follows a change at once.
        Settings.SettingsStore.Current.Changed += (_, change) =>
        {
            Backdrop.IsVisible = BackdropImage.Source is not null && change.New.CoverBackdrop;
            if (change.Old.PreviewDetails != change.New.PreviewDetails)
            {
                Update();
            }
        };
        DetailsButton.Click += (_, _) => Settings.SettingsStore.Current.Update(s => s with { PreviewDetails = !s.PreviewDetails });
        Player.HostsSurface = true;
        // The label style is in capitals (DESIGN.md §4).
        SelectedLabel.Text = L.T("Selected").ToUpper(System.Globalization.CultureInfo.CurrentUICulture);
    }

    public void Attach(PreviewModel model, LibraryModel library)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(library);
        _model = model;
        _library = library;
        HeaderButtons.Library = library;
        // Before the player hears of a move itself: it must not take the surface Quick Preview is about to get.
        model.SurfaceMoved += (_, _) => Player.HostsSurface = !model.IsQuickPreviewOpen;
        Player.Model = model;
        model.Changed += (_, _) => Update();
        library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryModel.SelectedBytes))
            {
                Update();
            }
        };
        Update();
    }

    private void Update()
    {
        if (_model is not { } model)
        {
            return;
        }
        var multiple = model.SelectionCount > 1;
        var asset = multiple ? null : model.Asset;
        Multiple.IsVisible = multiple;
        if (multiple)
        {
            CountText.Text = model.SelectionCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            BytesText.Text = ByteFormat.Format(_library?.SelectedBytes ?? 0);
        }
        Single.IsVisible = asset is not null;
        Facts.IsVisible = asset is not null;
        NothingText.IsVisible = !multiple && asset is null;
        if (asset is not null)
        {
            NameText.Text = asset.Name;
            Star.AssetId = asset.Id;
            FactsText.Text = PreviewFacts.Line(asset);
            DateText.Text = PreviewFacts.Date(asset);
        }
        // Read from the file only while the ⓘ is on.
        var details = Settings.SettingsStore.Current.Settings.PreviewDetails;
        Details.Show(details && asset is not null ? asset.Path : null, asset?.Kind ?? MediaKind.Photo);
        DetailsGlyph.Foreground = this.Brush(details ? JGColor.AccentText : JGColor.TextSecondary);
        // From song to song the blurred cover stays until the next one is known; a photo or a video clears it (MAC-028).
        if (asset?.Kind != MediaKind.Audio)
        {
            SetBackdrop(null);
        }
        else if (model.IsCoverKnown)
        {
            SetBackdrop(model.Cover);
        }
    }

    /// <summary>A tiny copy of the cover stretched over the pane blurs like frosted glass (as the audio viewer).</summary>
    private void SetBackdrop(Avalonia.Media.Imaging.Bitmap? cover)
    {
        if (ReferenceEquals(cover, _backdropFor))
        {
            return;
        }
        _backdropFor = cover;
        (BackdropImage.Source as IDisposable)?.Dispose();
        BackdropImage.Source = cover?.CreateScaledBitmap(new PixelSize(BackdropPixels, BackdropPixels), Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        Backdrop.IsVisible = cover is not null && Settings.SettingsStore.Current.Settings.CoverBackdrop;
    }

    /// <summary>The place it is in (its Move button goes to the other).</summary>
    public ModuleSlot Slot
    {
        get => HeaderButtons.Slot;
        set => HeaderButtons.Slot = value;
    }
}
