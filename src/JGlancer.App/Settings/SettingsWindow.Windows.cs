using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using JGlancer.Core.Localization;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Settings;

/// <summary>
/// Settings → Windows (W11): the app in Explorer (Open With, the menu entries, the Default Apps page; for the current
/// user only, removable) and Space in Explorer for Quick Preview (a helper that starts with Windows).
/// </summary>
internal sealed partial class SettingsWindow
{
    private List<Control> WindowsPage()
    {
        var executable = Environment.ProcessPath;
        var registration = executable is null ? null : ExplorerRegistration.ForCurrentUser(executable);
        var registered = registration?.IsRegistered == true;
        var defaultApps = new Button
        {
            Content = L.T("Default Apps…"),
            IsEnabled = registered,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        defaultApps.Click += (_, _) => Process.Start(new ProcessStartInfo(ExplorerRegistration.DefaultAppsPage.ToString()) { UseShellExecute = true });
        var explorer = SettingsUi.Group(L.T("Explorer"),
            SettingsUi.Toggle(L.T("Show jGlancer in Open With and the Explorer menu"), registered, on =>
            {
                if (registration is null)
                {
                    return;
                }
                if (on)
                {
                    registration.Register(DefaultApps.Texts);
                }
                else
                {
                    registration.Unregister();
                }
                Rebuild();
            }),
            SettingsUi.Text(L.T("Media files get “Quick Preview in jGlancer”, folders “Open in jGlancer”; in Windows 11 they are under “Show more options”. jGlancer never takes a file type by itself: Windows’ Default Apps can make it the default viewer."),
                "body", JGColor.TextTertiary, wraps: true),
            defaultApps);
        // The default app (W-80): a row per kind, a button where jGlancer is not it yet.
        var defaults = SettingsUi.Group(L.T("Default App"),
            SettingsUi.Text(L.T("Windows asks you to confirm: on the page that opens, press “Set default”."), "body", JGColor.TextTertiary, wraps: true));
        foreach (var kind in DefaultApps.Kinds)
        {
            Control state;
            if (DefaultApps.IsDefault(kind))
            {
                state = SettingsUi.Text(L.T("jGlancer"), "body", JGColor.TextSecondary);
            }
            else
            {
                var make = new Button { Content = L.T("Make Default…"), IsEnabled = executable is not null };
                Avalonia.Automation.AutomationProperties.SetName(make, L.T("Make Default…") + " " + DefaultApps.Title(kind));
                make.Click += (_, _) => DefaultApps.Offer(executable!, [kind]);
                state = make;
            }
            state.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = JGMetrics.ControlHeight };
            row.Children.Add(SettingsUi.Text(DefaultApps.Title(kind)));
            Grid.SetColumn(state, 1);
            row.Children.Add(state);
            defaults.Children.Add(row);
        }
        // Back from Windows' page: the rows show what was chosen there.
        if (!_watchesDefaults)
        {
            _watchesDefaults = true;
            Activated += (_, _) =>
            {
                if (_section == SettingsSection.Windows)
                {
                    Rebuild();
                }
            };
        }
        var space = SettingsUi.Group(L.T("Space in Explorer"),
            SettingsUi.Toggle(L.T("Space opens Quick Preview in jGlancer"), S.ExplorerSpacePreview, on => Set(s => s with { ExplorerSpacePreview = on })),
            SettingsUi.Toggle(L.T("While it is open, clicked files show in it"), S.ExplorerPreviewFollowsClicks, on => Set(s => s with { ExplorerPreviewFollowsClicks = on })),
            SettingsUi.Text(L.T("A small helper starts with Windows and waits for Space in Explorer’s file list; it stays in the notification area. Other files keep Explorer’s own Space."),
                "body", JGColor.TextTertiary, wraps: true));
        return [defaults, explorer, space];
    }

    private bool _watchesDefaults;
}
