using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Templates;
using Avalonia.Styling;

namespace JGlancer.Design;

/// <summary>
/// The app draws its own title bar (W-04): the client area extends over the system caption, and the decorations
/// Avalonia would draw there (title, caption buttons) are replaced by nothing. The system keeps the frame, the
/// rounded corners, resizing and snapping; the app marks its own title bar and caption buttons with
/// <see cref="WindowDecorationProperties"/> roles so Windows hit-tests them as native ones (drag, double-click,
/// snap layouts on the maximize button).
/// </summary>
public static class JGWindowChrome
{
    private static readonly ControlTheme EmptyDecorations = CreateTheme();

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ExtendClientAreaToDecorationsHint = true;
        window.WindowDecorationsTheme = EmptyDecorations;
    }

    private static ControlTheme CreateTheme()
    {
        var theme = new ControlTheme(typeof(WindowDrawnDecorations));
        theme.Setters.Add(new Setter(WindowDrawnDecorations.TemplateProperty, new EmptyTemplate()));
        return theme;
    }

    private sealed class EmptyTemplate : IWindowDrawnDecorationsTemplate
    {
        public TemplateResult<WindowDrawnDecorationsContent> Build() => new(new WindowDrawnDecorationsContent(), new NameScope());

        object? ITemplate.Build() => Build();
    }
}
