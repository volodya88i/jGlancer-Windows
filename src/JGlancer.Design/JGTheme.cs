using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace JGlancer.Design;

/// <summary>
/// Installs the design system into an application: one brush per color token for Dark, Light and their Increase
/// Contrast variants (theme dictionaries, so <c>{DynamicResource JG.bg.base}</c> follows the theme), the text-style
/// classes and the control themes of <c>JGStyles.axaml</c>.
/// </summary>
public static class JGTheme
{
    /// <summary>Dark with Increase Contrast; inherits every resource it does not override from Dark.</summary>
    public static ThemeVariant DarkIncreasedContrast { get; } = new("DarkIncreasedContrast", ThemeVariant.Dark);

    /// <summary>Light with Increase Contrast; inherits from Light.</summary>
    public static ThemeVariant LightIncreasedContrast { get; } = new("LightIncreasedContrast", ThemeVariant.Light);

    /// <summary>The Black theme (W-97): black surfaces; inherits every resource it does not override from Dark.</summary>
    public static ThemeVariant BlackTheme { get; } = new("Black", ThemeVariant.Dark);

    /// <summary>Black with Increase Contrast.</summary>
    public static ThemeVariant BlackIncreasedContrast { get; } = new("BlackIncreasedContrast", ThemeVariant.Dark);

    /// <summary>The Windows close-button red (<see cref="JGMetrics.CaptionCloseHover"/>) for XAML.</summary>
    public static Color CaptionCloseHoverColor { get; } = Color.FromUInt32(JGMetrics.CaptionCloseHover);

    /// <summary>Adds the token brushes, text styles and control themes to <paramref name="application"/>.</summary>
    public static void Install(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var resources = application.Resources;
        resources.ThemeDictionaries[ThemeVariant.Dark] = Brushes(isDark: true, increasedContrast: false);
        resources.ThemeDictionaries[ThemeVariant.Light] = Brushes(isDark: false, increasedContrast: false);
        resources.ThemeDictionaries[DarkIncreasedContrast] = Brushes(isDark: true, increasedContrast: true);
        resources.ThemeDictionaries[LightIncreasedContrast] = Brushes(isDark: false, increasedContrast: true);
        resources.ThemeDictionaries[BlackTheme] = BlackBrushes(increasedContrast: false);
        resources.ThemeDictionaries[BlackIncreasedContrast] = BlackBrushes(increasedContrast: true);
        application.Styles.Add(TextStyles());
        application.Styles.Add(new JGStyles());
    }

    /// <summary>The theme variant for a dark/light choice and the system's high-contrast setting.</summary>
    public static ThemeVariant Variant(bool isDark, bool increasedContrast) => (isDark, increasedContrast) switch
    {
        (true, true) => DarkIncreasedContrast,
        (true, false) => ThemeVariant.Dark,
        (false, true) => LightIncreasedContrast,
        (false, false) => ThemeVariant.Light,
    };

    /// <summary>The Black theme's variant for the system's high-contrast setting.</summary>
    public static ThemeVariant BlackVariant(bool increasedContrast) => increasedContrast ? BlackIncreasedContrast : BlackTheme;

    internal static ResourceDictionary Brushes(bool isDark, bool increasedContrast) => Brushes(token => token.Value(isDark, increasedContrast));

    private static ResourceDictionary BlackBrushes(bool increasedContrast) => Brushes(token => token.BlackValue(increasedContrast));

    private static ResourceDictionary Brushes(Func<JGColorToken, uint> value)
    {
        var dictionary = new ResourceDictionary();
        foreach (var token in JGColor.All)
        {
            dictionary[token.ResourceKey] = new ImmutableSolidColorBrush(Color.FromUInt32(value(token)));
        }
        SetAccentEntries(dictionary, value(JGColor.Accent));
        return dictionary;
    }

    /// <summary>
    /// The accent the user chose (W-93), in every theme, at once: bound brushes follow, self-drawn controls take it at their
    /// next render. Null brings back the tokens' own values.
    /// </summary>
    public static void SetAccent(Application application, uint? chosen)
    {
        ArgumentNullException.ThrowIfNull(application);
        foreach (var increasedContrast in new[] { false, true })
        {
            foreach (var isDark in new[] { true, false })
            {
                Apply(Variant(isDark, increasedContrast),
                    chosen ?? JGColor.Accent.Value(isDark, false),
                    chosen is { } color ? JGAccent.Text(color, isDark) : JGColor.AccentText.Value(isDark, false),
                    chosen is { } dimmed ? JGAccent.Dim(dimmed, isDark) : JGColor.AccentDim.Value(isDark, false));
            }
            Apply(BlackVariant(increasedContrast),
                chosen ?? JGColor.Accent.BlackValue(false),
                chosen is { } black ? JGAccent.TextBlack(black) : JGColor.AccentText.BlackValue(false),
                chosen is { } dim ? JGAccent.Dim(dim, isDark: true) : JGColor.AccentDim.BlackValue(false));
        }

        void Apply(ThemeVariant variant, uint accent, uint text, uint dim)
        {
            if (application.Resources.ThemeDictionaries[variant] is ResourceDictionary dictionary)
            {
                dictionary[JGColor.Accent.ResourceKey] = new ImmutableSolidColorBrush(Color.FromUInt32(accent));
                dictionary[JGColor.AccentText.ResourceKey] = new ImmutableSolidColorBrush(Color.FromUInt32(text));
                dictionary[JGColor.AccentDim.ResourceKey] = new ImmutableSolidColorBrush(Color.FromUInt32(dim));
                SetAccentEntries(dictionary, accent);
            }
        }
    }

    private static void SetAccentEntries(ResourceDictionary dictionary, uint argb)
    {
        var accent = Color.FromUInt32(argb);
        // Fluent's controls (switches, combo boxes, fields) use Windows' accent color: they take the app's instead.
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
            "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
        {
            dictionary[key] = accent;
        }
        // Check boxes and radio buttons (Duplicates, Setup): the accent box with a tick that reads on it, in every theme
        // (Fluent's light theme otherwise reached past the accent keys above to Windows' own color).
        var accentBrush = new ImmutableSolidColorBrush(accent);
        var tick = new ImmutableSolidColorBrush(Color.FromUInt32(JGAccent.On(argb)));
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            dictionary["CheckBoxCheckBackgroundFillChecked" + state] = accentBrush;
            dictionary["CheckBoxCheckBackgroundStrokeChecked" + state] = accentBrush;
            dictionary["CheckBoxCheckGlyphForegroundChecked" + state] = tick;
            dictionary["RadioButtonOuterEllipseCheckedFill" + state] = accentBrush;
            dictionary["RadioButtonOuterEllipseCheckedStroke" + state] = accentBrush;
            dictionary["RadioButtonCheckGlyphFill" + state] = tick;
            // Switches too: in the light theme they showed Windows' accent.
            dictionary["ToggleSwitchFillOn" + state] = accentBrush;
            dictionary["ToggleSwitchStrokeOn" + state] = accentBrush;
            dictionary["ToggleSwitchKnobFillOn" + state] = tick;
        }
    }

    /// <summary><c>TextBlock.display</c>, <c>.heading</c>, <c>.body</c>, <c>.button</c>, <c>.label</c>, <c>.mono</c>.</summary>
    internal static Styles TextStyles()
    {
        var styles = new Styles();
        foreach (var style in JGTypeScale.All)
        {
            // Is: also SelectableTextBlock (selectable names and facts).
            styles.Add(new Style(x => x.Is<TextBlock>().Class(style.ClassName))
            {
                Setters =
                {
                    new Setter(TextBlock.FontFamilyProperty, JGFonts.For(style.Family)),
                    new Setter(TextBlock.FontSizeProperty, style.Size),
                    new Setter(TextBlock.FontWeightProperty, style.Weight),
                    new Setter(TextBlock.LetterSpacingProperty, style.LetterSpacing),
                },
            });
        }
        return styles;
    }
}
