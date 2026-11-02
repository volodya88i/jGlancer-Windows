using System.Diagnostics;
using JGlancer.Platform.Windows.Setup;

namespace JGlancer.App.Setup;

/// <summary>
/// The app as its own installer and uninstaller (W-73). <c>jGlancer.exe --setup</c> (started by the setup stub from
/// the unpacked app) installs the folder it runs from; <c>--uninstall</c> (Apps &amp; features) copies the program to
/// a temporary folder and goes on from there, so the program folder can be deleted. Elevated runs for everyone on the
/// PC get the choices on their command line. Neither mode starts the library or touches the user's data.
/// </summary>
internal sealed record SetupMode(bool Uninstall, InstallScope? Scope, string? Target, bool Desktop, bool Elevated, string? From,
    IReadOnlyList<Core.Model.MediaKind> Defaults)
{
    public static SetupMode? Current { get; private set; }

    /// <summary>Recognizes the setup command line; null for a normal launch.</summary>
    public static SetupMode? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var setup = args.Contains("--setup", StringComparer.OrdinalIgnoreCase);
        var uninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase);
        if (!setup && !uninstall)
        {
            return null;
        }
        string? Value(string name)
        {
            var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        Current = new SetupMode(
            uninstall,
            Value("--scope") switch { "all" => InstallScope.AllUsers, "user" => InstallScope.CurrentUser, _ => null },
            Value("--target"),
            args.Contains("--desktop", StringComparer.OrdinalIgnoreCase),
            args.Contains("--elevated", StringComparer.OrdinalIgnoreCase),
            Value("--from"),
            // `--defaults photo,video,audio`: the kinds to offer Windows as jGlancer's (W-80).
            [.. (Value("--defaults") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(name => Enum.TryParse<Core.Model.MediaKind>(name, ignoreCase: true, out var kind) ? kind : (Core.Model.MediaKind?)null)
                .OfType<Core.Model.MediaKind>()]);
        return Current;
    }

    /// <summary>
    /// The uninstaller's own temporary copy goes once it has ended (a hidden command waits a moment, then removes
    /// the folder). Only a "jGlancer-Uninstall-…" folder in the temporary folder, never anything else.
    /// </summary>
    public void CleanUpAfterExit()
    {
        var here = AppContext.BaseDirectory.TrimEnd('\\');
        var temp = Path.GetTempPath().TrimEnd('\\');
        if (!Uninstall || From is null || !Path.GetFileName(here).StartsWith("jGlancer-Uninstall-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(here), temp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var start = new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{here}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(start)?.Dispose();
    }

    /// <summary>
    /// Uninstall from the program folder itself: copies the program to a temporary folder and starts it there (as
    /// administrator for an installation for everyone). True when this process should end now.
    /// </summary>
    public bool HandsOverUninstall()
    {
        if (!Uninstall || From is not null)
        {
            return false;
        }
        var here = AppContext.BaseDirectory.TrimEnd('\\');
        var temporary = Path.Combine(Path.GetTempPath(), "jGlancer-Uninstall-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var file in Directory.GetFiles(here, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(temporary, Path.GetRelativePath(here, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var forEveryone = InstallLayout.FindInstalled() is { Scope: InstallScope.AllUsers };
        var start = new ProcessStartInfo(Path.Combine(temporary, InstallLayout.ExecutableName), $"--uninstall --from \"{here}\"")
        {
            UseShellExecute = true,
            Verb = forEveryone && !Environment.IsPrivilegedProcess ? "runas" : "",
        };
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The administrator prompt was declined: nothing happens.
        }
        return true;
    }
}
