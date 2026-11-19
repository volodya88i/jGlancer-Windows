using Avalonia.Controls;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Settings;

/// <summary>Appearance → Effects → See through the photo viewer (MAC-023): Windows' acrylic, so only with its transparency effects.</summary>
internal sealed partial class SettingsWindow
{
    private Control SeeThroughToggle()
    {
        var toggle = SettingsUi.Toggle(L.T("See through the photo viewer"), S.SeeThroughPhotoViewer, on => Set(s => s with { SeeThroughPhotoViewer = on }));
        if (Platform.Windows.SystemTransparency.Enabled)
        {
            return toggle;
        }
        toggle.IsEnabled = false;
        return new StackPanel
        {
            Children =
            {
                toggle,
                SettingsUi.Text(L.T("Needs Windows' transparency effects: Settings → Personalization → Colors."), "body", JGColor.TextTertiary, wraps: true),
            },
        };
    }
}
