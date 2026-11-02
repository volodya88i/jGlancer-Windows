using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace JGlancer.Design;

/// <summary>
/// The one headline of a screen with an optional accent word (DESIGN.md §6): <c>**…**</c> in the localized string
/// marks the word drawn in <c>accent</c>, e.g. "Add a **folder**.".
/// </summary>
public sealed class JGAccentText : TextBlock
{
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<JGAccentText, string?>(nameof(Markup));

    /// <summary>Styles written for TextBlock (the text-style classes) apply to this control too.</summary>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    /// <summary>Splits markup into (text, isAccent) parts; unmatched markers are kept as plain text.</summary>
    public static IReadOnlyList<(string Text, bool IsAccent)> Parse(string? markup)
    {
        var parts = new List<(string, bool)>();
        if (string.IsNullOrEmpty(markup))
        {
            return parts;
        }
        var pieces = markup.Split("**");
        // An even number of pieces means an unclosed marker: treat the whole string as plain text.
        if (pieces.Length % 2 == 0)
        {
            parts.Add((markup, false));
            return parts;
        }
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].Length > 0)
            {
                parts.Add((pieces[i], i % 2 == 1));
            }
        }
        return parts;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkupProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        foreach (var (text, isAccent) in Parse(Markup))
        {
            var run = new Run(text);
            if (isAccent)
            {
                run.Bind(Run.ForegroundProperty, this.GetResourceObservable(JGColor.AccentText.ResourceKey));
            }
            inlines.Add(run);
        }
        Inlines = inlines;
    }
}
