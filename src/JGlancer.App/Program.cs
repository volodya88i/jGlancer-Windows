using Avalonia;
using Avalonia.Skia;
using JGlancer.App.Developer;

namespace JGlancer.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The app as its own installer and uninstaller (W-73): no library, no single-instance claim.
        if (Setup.SetupMode.Parse(args) is { } setup)
        {
            // Only the look (`--theme`, `--lang`) and `--snapshot` of the developer options apply here.
            DeveloperOptions.Current = DeveloperOptions.Parse(args);
            return setup.HandsOverUninstall() ? 0 : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        DeveloperOptions.Current = DeveloperOptions.Parse(args);
        // One app per library: a second launch hands its files to the running one and ends (W11).
        if (Services.SingleInstance.Claim(Services.AppPaths.For(DeveloperOptions.Current).DataDirectory, DeveloperOptions.Current.Launch) is not { } instance)
        {
            return 0;
        }
        Instance = instance;
        // Developer runs (snapshots, benchmarks) are silent: they play media nobody listens to.
        if (DeveloperOptions.Current.IsEphemeral)
        {
            JGlancer.Mpv.MpvPlayer.IsSilent = true;
        }
        // `--metadump <out> <files…>`: checks stage-2 COM interop inside the NativeAOT binary, without UI.
        if (args.Length > 2 && args[0] == "--metadump" && OperatingSystem.IsWindows())
        {
            File.WriteAllText(args[1], string.Concat(args.Skip(2).Select(Platform.Windows.MetadataDump.Describe)));
            return 0;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>This process as the one app (requests from later launches arrive here).</summary>
    public static Services.SingleInstance? Instance { get; private set; }

    /// <summary>Also used by the Avalonia designer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // GPU copies of drawn thumbnails are capped (W-37); the decoded ones live in the 96 MB bitmap cache anyway.
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = GpuResourceBytes });

    /// <summary>
    /// Skia's cache of GPU textures (drawn thumbnails, glyphs): about two screens of thumbnails at 3200 × 1200. At 64 MB
    /// scrolling re-uploads more (CPU +4 points), at 128 MB the process kept 200 MB more (W-37).
    /// </summary>
    private const long GpuResourceBytes = 96L * 1024 * 1024;
}
