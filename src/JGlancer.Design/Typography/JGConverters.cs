using System.Globalization;
using Avalonia.Data.Converters;

namespace JGlancer.Design;

/// <summary>Value converters for control templates.</summary>
public static class JGConverters
{
    /// <summary>
    /// A menu's shortcut hint as Windows writes it: <c>Ctrl+Alt+R</c>, and Enter (Avalonia names the key Return).
    /// </summary>
    public static IValueConverter KeyGesture { get; } =
        new FuncValueConverter<Avalonia.Input.KeyGesture?, string?>(gesture => gesture is null ? null : Display(gesture));

    /// <summary>
    /// A shortcut as Windows menus write it: modifiers in the order Ctrl, Alt, Shift, then the key's printed name
    /// (<c>Ctrl+Alt+0</c>, <c>Ctrl+,</c>, <c>Enter</c>, <c>Delete</c>).
    /// </summary>
    public static string Display(Avalonia.Input.KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var parts = new List<string>();
        if (gesture.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }
        if (gesture.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }
        if (gesture.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }
        parts.Add(KeyName(gesture.Key));
        return string.Join('+', parts);
    }

    private static string KeyName(Avalonia.Input.Key key) => key switch
    {
        >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => ((int)(key - Avalonia.Input.Key.D0)).ToString(CultureInfo.InvariantCulture),
        >= Avalonia.Input.Key.NumPad0 and <= Avalonia.Input.Key.NumPad9 => "Num " + ((int)(key - Avalonia.Input.Key.NumPad0)).ToString(CultureInfo.InvariantCulture),
        Avalonia.Input.Key.Enter => "Enter",
        Avalonia.Input.Key.OemComma => ",",
        Avalonia.Input.Key.OemPeriod => ".",
        Avalonia.Input.Key.OemPlus => "+",
        Avalonia.Input.Key.OemMinus => "-",
        Avalonia.Input.Key.OemQuestion => "/",
        Avalonia.Input.Key.OemSemicolon => ";",
        Avalonia.Input.Key.OemQuotes => "'",
        Avalonia.Input.Key.OemOpenBrackets => "[",
        Avalonia.Input.Key.OemCloseBrackets => "]",
        Avalonia.Input.Key.OemPipe or Avalonia.Input.Key.OemBackslash => "\\",
        Avalonia.Input.Key.OemTilde => "`",
        Avalonia.Input.Key.Back => "Backspace",
        Avalonia.Input.Key.PageUp => "PgUp",
        Avalonia.Input.Key.PageDown => "PgDn",
        _ => key.ToString(),
    };
}
