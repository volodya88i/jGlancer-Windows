using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// HDR video (W-87). The picture inside the UI (W-33) can only be SDR: the app's window has an 8-bit
/// swap chain. With HDR on (the HDR button, remembered), mpv draws the video itself, through Direct3D 11 with the
/// display's colour space, into a child window between the top bar and the controls; on an HDR display (Windows HDR
/// on) HDR10 and HLG videos show at their own brightness, elsewhere mpv tone-maps them as before. The chrome then
/// stays visible beside the picture instead of fading over it, and the filmstrip hides (nothing can lie over a child
/// window). The child window is disabled, so clicks, the wheel and drags reach the viewer under it as before.
/// The button shows only for HDR video (PQ or HLG, as mpv reads it); SDR video always takes the usual path.
/// </summary>
internal sealed partial class MediaViewerView
{
    private NativeVideoHost? _hdrHost;

    private static bool HdrWanted => Settings.SettingsStore.Current.Settings.HdrVideo;

    private bool IsHdr => _hdrHost is not null;

    private void ToggleHdr()
    {
        Settings.SettingsStore.Current.Update(s => s with { HdrVideo = !s.HdrVideo });
        ApplyHdr();
    }

    /// <summary>Puts the video where the setting wants it (a video viewer on screen only).</summary>
    private void ApplyHdr()
    {
        var wanted = HdrWanted && IsHdrVideo && _model is { Kind: MediaKind.Video } && _video is not null && VisualRoot is not null;
        if (wanted && !IsHdr)
        {
            EnterHdr();
        }
        else if (!wanted && IsHdr)
        {
            LeaveHdr();
        }
        UpdateHdrButton();
    }

    private void EnterHdr()
    {
        if (_model is not { } model || TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle is not { } parent || parent == 0)
        {
            return;
        }
        try
        {
            _hdrHost = NativeVideoHost.Create(parent);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        if (!model.Playback.Player.UseOwnWindow(_hdrHost.Handle))
        {
            _hdrHost.Dispose();
            _hdrHost = null;
            return;
        }
        FilmstripArea.IsVisible = false;
        ShowChrome();
        LayoutUpdated += OnHdrLayout;
        PlaceHdrHost();
    }

    /// <summary>Back to the picture inside the UI; also before the viewer goes (mpv must let go of the window first).</summary>
    private void LeaveHdr()
    {
        if (_hdrHost is not { } host)
        {
            return;
        }
        LayoutUpdated -= OnHdrLayout;
        _model?.Playback.Player.UseOwnWindow(0);
        host.Dispose();
        _hdrHost = null;
        FilmstripArea.IsVisible = true;
        ShowChrome();
    }

    private void OnHdrLayout(object? sender, EventArgs e) => PlaceHdrHost();

    /// <summary>The child window covers the area between the top bar and the controls, in physical pixels.</summary>
    private void PlaceHdrHost()
    {
        if (_hdrHost is not { } host || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        var scale = top.RenderScaling;
        var topEdge = TopBar.TranslatePoint(new Point(0, TopBar.Bounds.Height), top);
        var bottomEdge = ControlsPanel.TranslatePoint(new Point(0, 0), top);
        var left = Root.TranslatePoint(new Point(0, 0), top);
        if (topEdge is not { } a || bottomEdge is not { } b || left is not { } c)
        {
            return;
        }
        var x = (int)Math.Round(c.X * scale);
        var y = (int)Math.Round(a.Y * scale);
        var width = (int)Math.Round(Root.Bounds.Width * scale);
        var height = (int)Math.Round((b.Y - a.Y) * scale);
        host.Place(x, y, width, height, IsEffectivelyVisible && height > 0);
    }

    /// <summary>The video shown is HDR: PQ (HDR10, Dolby Vision's base) or HLG transfer, once its frames are known.</summary>
    private bool IsHdrVideo { get; set; }

    private string? _hdrReadFor;

    /// <summary>
    /// Asks mpv once per file, when its picture is known, off the UI thread (W-100). Until the answer comes the last one
    /// stands, so a row of HDR videos does not leave and re-enter HDR at each one.
    /// </summary>
    private async void ReadHdrVideo()
    {
        if (_model is not { Kind: MediaKind.Video } model)
        {
            IsHdrVideo = false;
        }
        else if (model.Playback.VideoSize.Width > 0 && model.Current?.Path is { } path && path != _hdrReadFor)
        {
            _hdrReadFor = path;
            var gamma = await model.Playback.Player.ReadAsync(player => player.GetString("video-params/gamma"));
            if (ReferenceEquals(model, _model) && path == _hdrReadFor)
            {
                IsHdrVideo = gamma is "pq" or "hlg";
                ApplyHdr();
            }
        }
    }

    private void UpdateHdrButton()
    {
        HdrButton.IsVisible = IsHdrVideo;
        HdrButton.Classes.Set("on", HdrWanted);
        var tip = HdrWanted
            ? L.T("HDR on: the video shows at its own brightness when Windows HDR is on")
            : L.T("HDR off: HDR video is shown in standard range");
        ToolTip.SetTip(HdrButton, tip);
        Avalonia.Automation.AutomationProperties.SetName(HdrButton, L.T("HDR"));
    }

    /// <summary>`--hdr-check`: HDR on for a while, then off, with mpv's output and colour facts logged; closes the window.</summary>
    public async Task RunHdrCheckAsync(string log)
    {
        string[] names = ["current-vo", "video-params/gamma", "video-params/primaries", "video-params/sig-peak",
            "video-target-params/gamma", "video-target-params/primaries", "frame-drop-count", "vo-delayed-frame-count", "time-pos"];
        var lines = new List<string>();
        void Facts(string when)
        {
            lines.Add($"{when}: hdr={IsHdr} host={_hdrHost?.Handle}");
            lines.AddRange(names.Select(name => $"  {name}={_model?.Playback.Player.GetString(name)}"));
        }
        await Task.Delay(TimeSpan.FromSeconds(3));
        Facts("start");
        if (!HdrWanted)
        {
            ToggleHdr();
        }
        await Task.Delay(TimeSpan.FromSeconds(4));
        Facts("hdr on");
        ToggleHdr();
        await Task.Delay(TimeSpan.FromSeconds(3));
        Facts("hdr off");
        await File.WriteAllLinesAsync(log, lines);
        (TopLevel.GetTopLevel(this) as Window)?.Close();
    }

    /// <summary>The render context exists (or another file opened): HDR follows the setting.</summary>
    private void ApplyHdrSoon() => Dispatcher.UIThread.Post(ApplyHdr, DispatcherPriority.Background);
}
