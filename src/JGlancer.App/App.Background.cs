using Avalonia.Controls;
using Avalonia.Platform;
using JGlancer.App.Developer;
using JGlancer.App.Preview;
using JGlancer.App.Settings;
using JGlancer.Core.Localization;
using JGlancer.Platform.Windows;

namespace JGlancer.App;

/// <summary>
/// Space in Explorer (Settings → Windows; W11): while it is on, this process keeps the helper's hook and stays in the
/// notification area after the library window closes; it starts with Windows (<c>--background</c>, no window).
/// Turning it off removes both; an app with no window then ends.
/// </summary>
internal sealed partial class App
{
    private ExplorerSpaceHelper? _spaceHelper;
    private TrayIcon? _trayIcon;

    /// <summary>Starts or stops the helper and its start at sign-in as the setting says.</summary>
    private void ApplySpaceHelper()
    {
        var enabled = SettingsStore.Current.Settings.ExplorerSpacePreview;
        // Developer runs never change what starts with Windows.
        if (!DeveloperOptions.Current.IsEphemeral && Environment.ProcessPath is { } executable)
        {
            var login = LoginStart.ForCurrentUser(executable);
            if (enabled != login.IsEnabled)
            {
                if (enabled)
                {
                    login.Enable();
                }
                else
                {
                    login.Disable();
                }
            }
        }
        if (enabled && _spaceHelper is null)
        {
            _spaceHelper = new ExplorerSpaceHelper(ShowFromSpaceAsync, new ExplorerPreviewAccess(() => _explorerPreview))
            {
                FollowsClicks = SettingsStore.Current.Settings.ExplorerPreviewFollowsClicks,
            };
            ShowTrayIcon();
        }
        else if (!enabled && _spaceHelper is not null)
        {
            _spaceHelper.Dispose();
            _spaceHelper = null;
            HideTrayIcon();
            ShutDownIfIdle();
        }
    }

    /// <summary>Space in Explorer: the preview without taking the keyboard from Explorer.</summary>
    private async Task<bool> ShowFromSpaceAsync(IReadOnlyList<string> files, string focus)
    {
        await ShowExplorerPreviewAsync(files, focus, activate: false);
        return _explorerPreview?.IsOpen == true;
    }

    private void ShowTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }
        var open = new NativeMenuItem(L.T("Open jGlancer"));
        open.Click += (_, _) => _ = HandleLaunchAsync(Core.Launch.LaunchArguments.None);
        var quit = new NativeMenuItem(L.T("Quit jGlancer"));
        quit.Click += (_, _) => _desktop?.Shutdown();
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://jGlancer/Assets/jGlancer.ico"))),
            ToolTipText = L.T("jGlancer: Space shows a Quick Preview in Explorer"),
            Menu = [open, new NativeMenuItemSeparator(), quit],
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => _ = HandleLaunchAsync(Core.Launch.LaunchArguments.None);
        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    private void HideTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
        TrayIcon.SetIcons(this, []);
    }

    private void StopSpaceHelper()
    {
        _spaceHelper?.Dispose();
        _spaceHelper = null;
        HideTrayIcon();
    }
}
