using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// Folder Icon… (macOS FolderIconPicker, D-129): a library folder's icon and name, a grid of 48 symbols in 8 columns, an
/// Emoji field (the last emoji typed or picked becomes the icon; Emoji &amp; Symbols opens Windows' emoji panel), Choose
/// Photo… (any picture file), Default (the disk again) and Done. Each choice applies at once.
/// </summary>
internal sealed class FolderIconPicker : Window
{
    private const int Columns = 8;

    private readonly LibraryModel _model;
    private readonly RootFolder _root;
    private readonly FolderIconView _header;
    private readonly List<(string Name, Button Button, JGIcon Glyph)> _symbols = [];
    private readonly TextBox _emoji;

    public FolderIconPicker(LibraryModel model, RootFolder root)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(root);
        _model = model;
        _root = root;
        Title = L.T("Folder Icon");
        Width = JGMetrics.PreviewPaneMaxWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();

        _header = new FolderIconView { Side = JGMetrics.FolderIconButtonSide, Fallback = JGIconData.HardDrive, Foreground = this.Brush(JGColor.TextPrimary) };
        var heading = Text(L.T("Folder Icon"), "heading", JGColor.TextPrimary);
        var name = Text(Path.GetFileName(root.Path.TrimEnd('\\', '/')) is { Length: > 0 } leaf ? leaf : root.Path, "body", JGColor.TextSecondary);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = JGSpacing.S,
            Children = { _header, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { heading, name } } },
        };

        var grid = new UniformGrid { Columns = Columns };
        foreach (var (symbol, data) in FolderIconSymbols.All)
        {
            var glyph = new JGIcon { Data = data, Width = JGMetrics.IconSize + 2, Height = JGMetrics.IconSize + 2 };
            var button = new Button
            {
                Classes = { "icon" },
                Width = JGMetrics.FolderIconButtonSide,
                Height = JGMetrics.FolderIconButtonSide,
                Padding = default,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, JGSpacing.Xxs),
                Content = glyph,
            };
            AutomationProperties.SetName(button, symbol);
            button.Click += async (_, _) => await _model.SetRootIconAsync(_root.Id, new FolderIcon.Symbol(symbol));
            _symbols.Add((symbol, button, glyph));
            grid.Children.Add(button);
        }

        _emoji = new TextBox { Width = JGMetrics.FolderIconButtonSide * 2, PlaceholderText = L.T("Emoji"), FontFamily = JGFonts.Emoji };
        AutomationProperties.SetName(_emoji, L.T("Emoji"));
        _emoji.TextChanged += async (_, _) => await UseEmojiAsync();
        var emojiPanel = new Button { Content = L.T("Emoji & Symbols"), Classes = { "tertiary" } };
        emojiPanel.Click += (_, _) =>
        {
            _emoji.Focus();
            Dispatcher.UIThread.Post(() => Platform.Windows.EmojiPanel.Open(), DispatcherPriority.Input);
        };
        var choose = new Button { Content = L.T("Choose Photo…"), Classes = { "secondary" } };
        choose.Click += async (_, _) => await ChoosePhotoAsync();
        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(choose, Dock.Right);
        row.Children.Add(choose);
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xs, Children = { _emoji, emojiPanel } });

        var reset = new Button { Content = L.T("Default"), Classes = { "tertiary" } };
        reset.Click += async (_, _) =>
        {
            _emoji.Text = "";
            await _model.SetRootIconAsync(_root.Id, null);
        };
        var done = new Button { Content = L.T("Done"), Classes = { "primary" }, IsDefault = true };
        done.Click += (_, _) => Close();
        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(done, Dock.Right);
        buttons.Children.Add(done);
        buttons.Children.Add(reset);

        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.M,
            Children = { header, grid, row, buttons },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        model.RootIconsChanged += OnIconsChanged;
        Closed += (_, _) => model.RootIconsChanged -= OnIconsChanged;
        ShowCurrent();
    }

    private TextBlock Text(string text, string style, JGColorToken color)
    {
        var block = new TextBlock { Text = text, Classes = { style } };
        block[!TextBlock.ForegroundProperty] = this.GetResourceObservable(color.ResourceKey).ToBinding();
        return block;
    }

    private void OnIconsChanged(object? sender, EventArgs e) => ShowCurrent();

    /// <summary>The header shows the icon now; the current symbol stands out in the grid.</summary>
    private void ShowCurrent()
    {
        var current = _model.RootIcons.GetValueOrDefault(_root.Id);
        _header.Icon = current;
        foreach (var (name, button, glyph) in _symbols)
        {
            var isCurrent = current?.SymbolData is { } data && data == FolderIconSymbols.Data(name);
            button.Background = isCurrent ? this.Brush(JGColor.BgRaised) : Brushes.Transparent;
            glyph.Foreground = this.Brush(isCurrent ? JGColor.TextPrimary : JGColor.TextSecondary);
        }
    }

    /// <summary>The last emoji typed or picked becomes the icon; the field keeps only it.</summary>
    private async Task UseEmojiAsync()
    {
        if (FolderIcon.LastEmoji(_emoji.Text) is not { } emoji)
        {
            return;
        }
        if (_emoji.Text != emoji)
        {
            _emoji.Text = emoji;
            _emoji.CaretIndex = emoji.Length;
        }
        await _model.SetRootIconAsync(_root.Id, new FolderIcon.Emoji(emoji));
    }

    private async Task ChoosePhotoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await _model.SetRootIconFromFileAsync(_root.Id, path);
        }
    }
}
