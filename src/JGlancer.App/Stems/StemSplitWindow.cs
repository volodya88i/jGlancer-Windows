using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using JGlancer.App.Settings;
using JGlancer.Core.Localization;
using JGlancer.Core.Stems;
using JGlancer.Design;

namespace JGlancer.App.Stems;

/// <summary>What the split window decided: the folder (null: next to each song) and the stems.</summary>
internal sealed record StemSplitRequest(string? Folder, StemChoice Choice);

/// <summary>
/// Split into Stems… (W-85, macOS docs/windows/STEM_SPLIT.md): where the stems go (a new "&lt;song&gt; split" folder
/// next to the song by default, or a chosen folder), 4 or 6 stems, and which of them to write (at least one). The
/// mode and ticks are remembered by the caller, the folder is not. Several songs share the settings; each then gets
/// its own "… split" folder, next to it or inside the chosen one.
/// </summary>
internal sealed class StemSplitWindow : Window
{
    private readonly IReadOnlyList<string> _songs;
    private readonly TextBlock _where;
    private readonly StackPanel _ticks = new() { Spacing = JGSpacing.Xxs };
    private readonly TextBlock _note;
    private readonly Button _split = new() { Content = L.T("Split"), IsDefault = true, Classes = { "primary" } };
    private string? _folder;
    private StemChoice _choice;

    private StemSplitWindow(IReadOnlyList<string> songs, StemChoice choice)
    {
        _songs = songs;
        _choice = choice;
        Title = L.T("Split into Stems");
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        JGWindowChrome.Apply(this);

        var subject = songs.Count == 1 ? Path.GetFileName(songs[0]) : L.Plural("{0} songs", songs.Count);
        var heading = SettingsUi.Text(L.T("Split “{0}” into Stems", subject), "heading", JGColor.TextPrimary);
        heading.TextWrapping = TextWrapping.Wrap;
        _where = SettingsUi.Text("", "mono", JGColor.TextSecondary);
        var choose = new Button { Content = L.T("Choose…") };
        choose.Click += async (_, _) => await ChooseFolderAsync();
        var whereRow = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = JGSpacing.S };
        whereRow.Children.Add(_where);
        Avalonia.Controls.Grid.SetColumn(choose, 1);
        whereRow.Children.Add(choose);

        var mode = SettingsUi.Segmented(L.T("Stems"), [(L.T("4 stems"), StemMode.Four), (L.T("6 stems"), StemMode.Six)], choice.Mode, value =>
        {
            _choice = _choice.WithMode(value);
            ShowTicks();
        });
        _note = SettingsUi.Text(L.T("6 stems also separate guitar and piano; piano is the least clean."), "body", JGColor.TextTertiary, wraps: true);

        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        _split.Click += (_, _) => Close(new StemSplitRequest(_folder, _choice));
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.M,
            Children =
            {
                heading,
                SettingsUi.Group(L.T("Save To"), whereRow),
                SettingsUi.Group(L.T("Stems"), mode, _ticks, _note),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = JGSpacing.Xs,
                    Children = { cancel, _split },
                },
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
        ShowFolder();
        ShowTicks();
    }

    public static Task<StemSplitRequest?> AskAsync(Window owner, IReadOnlyList<string> songs, StemChoice choice) =>
        new StemSplitWindow(songs, choice).ShowDialog<StemSplitRequest?>(owner);

    private void ShowFolder()
    {
        string shown;
        if (_folder is not null)
        {
            shown = _songs.Count == 1 ? _folder : Path.Combine(_folder, L.T("<song> split"));
        }
        else
        {
            shown = _songs.Count == 1 ? StemNaming.DefaultFolder(_songs[0], Directory.Exists) : L.T("Next to each song, in “<song> split”");
        }
        _where.Text = shown;
        ToolTip.SetTip(_where, shown);
    }

    private async Task ChooseFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = L.T("Save To") });
        if (folders is [var folder] && folder.TryGetLocalPath() is { } path)
        {
            _folder = path;
            ShowFolder();
        }
    }

    private void ShowTicks()
    {
        _ticks.Children.Clear();
        foreach (var stem in StemModelInfo.Shown(_choice.Mode))
        {
            var box = new CheckBox { Content = StemTitle(stem), IsChecked = _choice.Ticked.Contains(stem) };
            box.IsCheckedChanged += (_, _) =>
            {
                _choice = _choice.With(stem, box.IsChecked == true);
                // The last ticked stem stays ticked.
                box.IsChecked = _choice.Ticked.Contains(stem);
                _split.IsEnabled = _choice.CanSplit;
            };
            _ticks.Children.Add(box);
        }
        _note.IsVisible = _choice.Mode == StemMode.Six;
        _split.IsEnabled = _choice.CanSplit;
    }

    /// <summary>A stem's name in the app's language.</summary>
    public static string StemTitle(StemKind stem) => stem switch
    {
        StemKind.Vocals => L.T("Vocals"),
        StemKind.Drums => L.T("Drums"),
        StemKind.Bass => L.T("Bass"),
        StemKind.Guitar => L.T("Guitar"),
        StemKind.Piano => L.T("Piano"),
        _ => L.T("Other|stem"),
    };
}
