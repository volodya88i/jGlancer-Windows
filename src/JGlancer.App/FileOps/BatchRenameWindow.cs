using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Core.FileOps;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.FileOps;

/// <summary>
/// Rename… with several files (macOS BatchRenameSheet, FEATURES §7): a template of text and tokens (name, number,
/// date), the first number and its digits, one extension for all, and the list of old and new names with any problem
/// marked. Rename runs as one undoable batch.
/// </summary>
internal sealed class BatchRenameWindow : Window
{
    /// <summary>Rows the preview lists (the rest are counted).</summary>
    private const int PreviewLimit = 300;

    private readonly IReadOnlyList<BatchRenamePattern.File> _files;
    private readonly Dictionary<string, IReadOnlySet<string>> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _template = new() { Text = BatchRenamePattern.NameToken };
    private readonly NumericUpDown _start = new() { Minimum = 0, Maximum = 999_999, Value = 1, Increment = 1, FormatString = "0", MinWidth = 120 };
    private readonly ComboBox _digits = new() { MinWidth = 120 };
    private readonly TextBox _extension = new();
    private readonly TextBlock _note = new() { Classes = { "body" }, Text = L.T("Only the name changes: the file is not converted."), IsVisible = false };
    private readonly StackPanel _rows = new() { Spacing = JGSpacing.Xxs };
    private readonly Button _rename = new() { Content = L.T("Rename"), IsDefault = true, Classes = { "primary" } };
    private IReadOnlyList<string> _names = [];

    private BatchRenameWindow(IReadOnlyList<BatchRenamePattern.File> files)
    {
        _files = files;
        Title = L.T("Rename {0} Files", files.Count);
        Width = JGMetrics.BatchRenameWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        foreach (var folder in files.Select(f => Path.GetDirectoryName(f.Path) ?? "").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _listings[folder] = Directory.Exists(folder)
                ? Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
        }
        _digits.ItemsSource = new[] { L.T("Digits: auto") }.Concat(Enumerable.Range(2, 4).Select(d => L.T("Digits: {0}", d))).ToList();
        _digits.SelectedIndex = 0;
        _extension.PlaceholderText = L.T("New extension (empty keeps each file's own)");
        _template.PlaceholderText = L.T("Template");
        Avalonia.Automation.AutomationProperties.SetName(_template, L.T("Template"));
        Avalonia.Automation.AutomationProperties.SetName(_extension, L.T("New extension (empty keeps each file's own)"));

        var heading = Text(Title, "heading", JGColor.TextPrimary);
        var tokens = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xs };
        foreach (var (label, token) in new[] { (L.T("Name"), BatchRenamePattern.NameToken), (L.T("Number"), BatchRenamePattern.CounterToken), (L.T("Date"), BatchRenamePattern.DateToken) })
        {
            var button = new Button { Content = label, Classes = { "secondary" } };
            ToolTip.SetTip(button, token);
            button.Click += (_, _) =>
            {
                _template.Text += token;
                _template.Focus();
                _template.CaretIndex = _template.Text?.Length ?? 0;
            };
            tokens.Children.Add(button);
        }
        var startLabel = Text(L.T("Start at {0}", "").Trim(), "body", JGColor.TextSecondary);
        startLabel.VerticalAlignment = VerticalAlignment.Center;
        var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.S, Children = { startLabel, _start, _digits } };
        _note[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        var preview = new Border
        {
            Height = JGMetrics.BatchRenameListHeight,
            Padding = new Thickness(JGSpacing.Xs),
            CornerRadius = new CornerRadius(JGMetrics.RadiusSmall),
            Child = new ScrollViewer { Content = _rows },
        };
        preview[!Border.BackgroundProperty] = this.GetResourceObservable(JGColor.BgBase.ResourceKey).ToBinding();
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, Classes = { "secondary" } };
        cancel.Click += (_, _) => Close(null);
        _rename.Click += (_, _) => Close(Changes());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = JGSpacing.Xs, Children = { cancel, _rename } };
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.M,
            Children = { heading, new StackPanel { Spacing = JGSpacing.Xs, Children = { _template, tokens } }, numbers, new StackPanel { Spacing = JGSpacing.Xxs, Children = { _extension, _note } }, preview, buttons },
        };
        _template.TextChanged += (_, _) => Update();
        _extension.TextChanged += (_, _) => Update();
        _start.ValueChanged += (_, _) => Update();
        _digits.SelectionChanged += (_, _) => Update();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
        Opened += (_, _) =>
        {
            _template.Focus();
            _template.SelectAll();
        };
        Update();
    }

    /// <summary>The renames chosen (only files whose name changes), or null when cancelled.</summary>
    public static Task<IReadOnlyList<(string Source, string Name)>?> AskAsync(Window owner, IReadOnlyList<BatchRenamePattern.File> files) =>
        new BatchRenameWindow(files).ShowDialog<IReadOnlyList<(string Source, string Name)>?>(owner);

    private BatchRenamePattern Pattern => new()
    {
        Template = _template.Text ?? "",
        Start = (int)(_start.Value ?? 1),
        Digits = _digits.SelectedIndex <= 0 ? 0 : _digits.SelectedIndex + 1,
        NewExtension = _extension.Text ?? "",
    };

    private List<(string Source, string Name)> Changes() =>
        [.. _files.Zip(_names).Where(p => Path.GetFileName(p.First.Path) != p.Second).Select(p => (p.First.Path, p.Second))];

    private void Update()
    {
        var pattern = Pattern;
        _names = pattern.Names(_files);
        var problems = BatchRenamePattern.Problems(_files, _names, folder => _listings.GetValueOrDefault(folder) ?? new HashSet<string>());
        _note.IsVisible = (_extension.Text ?? "").Trim(' ', '.').Length > 0;
        _rename.IsEnabled = problems.All(p => p is null) && Changes().Count > 0;
        _rows.Children.Clear();
        for (var i = 0; i < Math.Min(_files.Count, PreviewLimit); i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = JGSpacing.Xs };
            var old = Text(Path.GetFileName(_files[i].Path), "mono", JGColor.TextSecondary);
            var arrow = Text("→", "mono", JGColor.TextTertiary);
            Grid.SetColumn(arrow, 1);
            var shown = Text(problems[i] is { } problem ? Message(problem) : _names[i], "mono", problems[i] is null ? JGColor.TextPrimary : JGColor.Danger);
            Grid.SetColumn(shown, 2);
            row.Children.Add(old);
            row.Children.Add(arrow);
            row.Children.Add(shown);
            _rows.Children.Add(row);
        }
        if (_files.Count > PreviewLimit)
        {
            _rows.Children.Add(Text("… +" + (_files.Count - PreviewLimit).ToString("N0", CultureInfo.CurrentCulture), "mono", JGColor.TextTertiary));
        }
    }

    private static string Message(BatchRenamePattern.Problem problem) => problem switch
    {
        BatchRenamePattern.Problem.Empty => L.T("No name"),
        BatchRenamePattern.Problem.InvalidCharacters => L.T("Can't use < > : \" / \\ | ? * or a device name"),
        BatchRenamePattern.Problem.Duplicate => L.T("Same name twice"),
        _ => L.T("Name taken"),
    };

    private TextBlock Text(string text, string style, JGColorToken color)
    {
        var block = new TextBlock { Text = text, Classes = { style }, TextTrimming = TextTrimming.CharacterEllipsis };
        block[!TextBlock.ForegroundProperty] = this.GetResourceObservable(color.ResourceKey).ToBinding();
        return block;
    }
}
