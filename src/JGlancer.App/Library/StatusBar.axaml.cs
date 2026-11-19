using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The status bubble at the bottom of a pane (macOS MAC-025, MAC-037): "N items · size" or "N of M selected · size",
/// then the activity ("Indexing…"), or the latest problem with ×. Invisible while it has nothing to say.
/// </summary>
internal sealed partial class StatusBar : UserControl
{
    private const string Separator = "  ·  ";
    private LibraryModel? _model;

    public StatusBar()
    {
        InitializeComponent();
        Support.WindowGlass.FrostLive(Bubble, JGColor.BgRaised, JGMetrics.StatusBubbleTintOpacity);
        DismissButton.Click += (_, _) =>
        {
            if (DataContext is LibraryModel model)
            {
                model.Problem = LibraryProblem.None;
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }
        _model = DataContext as LibraryModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }
        Update();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryModel.StatusText) or nameof(LibraryModel.ActivityText) or nameof(LibraryModel.ProblemText)
            or null or "")
        {
            Update();
        }
    }

    private void Update()
    {
        var status = _model?.StatusText ?? "";
        var activity = _model?.ActivityText ?? "";
        var problem = _model?.ProblemText ?? "";
        var inlines = new InlineCollection();
        var parts = status.Split(Separator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (problem.Length == 0 && activity.Length > 0)
        {
            parts.Add("\0" + activity);
        }
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(Run(Separator, JGColor.TextSecondary));
            }
            var isActivity = parts[i].StartsWith('\0');
            inlines.Add(Run(isActivity ? parts[i][1..] : parts[i], isActivity ? JGColor.TextSecondary : JGColor.TextPrimary));
        }
        Counts.Inlines = inlines;
        Counts.IsVisible = parts.Count > 0;
        Bubble.IsVisible = parts.Count > 0 || problem.Length > 0;
    }

    private Run Run(string text, JGColorToken color)
    {
        var run = new Run(text);
        run[!TextElement.ForegroundProperty] = this.GetResourceObservable(color.ResourceKey).ToBinding();
        return run;
    }
}
