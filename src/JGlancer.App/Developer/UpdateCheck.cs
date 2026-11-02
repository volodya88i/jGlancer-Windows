using Avalonia.Controls;
using JGlancer.App.Services;
using JGlancer.Core.Updates;

namespace JGlancer.App.Developer;

/// <summary>
/// `--update-check &lt;log&gt;` (W-101): asks GitHub for the latest release, downloads and verifies it whatever its version,
/// then starts the installer offscreen (`--setup --snapshot`, which installs nothing) to show that a verified file can
/// be started. Logs each step; quits.
/// </summary>
internal static class UpdateCheck
{
    public static void Attach(Window window, string log) => window.Opened += async (_, _) =>
    {
        var lines = new List<string>();
        try
        {
            var release = await UpdateService.LatestAsync(CancellationToken.None);
            lines.Add($"latest: {release?.VersionText ?? "none"}");
            lines.Add($"newer than 0.0.1: {release is not null && AppUpdates.IsNewer(release, "0.0.1")}");
            if (release is not null)
            {
                using var installer = await UpdateService.DownloadAsync(release, (_, _) => { }, CancellationToken.None);
                lines.Add($"verified: {Path.GetFileName(installer.Path)} {new FileInfo(installer.Path).Length}");
                var shot = log + ".setup.png";
                using var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer.Path) { ArgumentList = { "--snapshot", shot }, UseShellExecute = false });
                if (started is not null)
                {
                    await started.WaitForExitAsync();
                    lines.Add($"started, exit {started.ExitCode}, window drawn: {File.Exists(shot)}");
                }
                var folder = Path.GetDirectoryName(installer.Path);
                installer.Dispose();
                if (folder is not null)
                {
                    UpdateService.RemoveFolder(folder);
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException or OperationCanceledException)
        {
            lines.Add($"failed: {error.GetType().Name}: {error.Message}");
        }
        await File.WriteAllLinesAsync(log, lines);
        window.Close();
    };
}
