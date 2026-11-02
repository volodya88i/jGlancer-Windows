using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace JGlancer.Design;

/// <summary>
/// A section label such as FOLDERS or PREVIEW (DESIGN.md §6): the <c>label</c> style in <c>text.tertiary</c>, shown in
/// capitals. Uppercasing is a display transform of the localized text, using the UI culture so Russian stays correct.
/// </summary>
public sealed class JGSectionHeader : TextBlock
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<JGSectionHeader, string?>(nameof(Label));

    public JGSectionHeader()
    {
        Classes.Add(JGTypeScale.Label.ClassName);
        Classes.Add("section");
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
        {
            Text = Label?.ToUpper(CultureInfo.CurrentUICulture);
        }
    }
}
