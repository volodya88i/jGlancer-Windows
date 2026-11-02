using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Settings;
using JGlancer.App.Shell;
using JGlancer.Core.Updates;

namespace JGlancer.App.Services;

/// <summary>An installer that passed its signature check; held open, so it cannot be swapped before it starts.</summary>
internal sealed class VerifiedInstaller(string path, FileStream held) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose() => held.Dispose();
}

/// <summary>
/// Looks for a newer version on GitHub and fetches it (W-101). The request carries no data about the user's files or
/// library. A failed check is silent at start; Settings shows it when asked by hand.
/// </summary>
internal static class UpdateService
{
    private const string FolderPrefix = "jGlancer-Update-";
    private const long MaximumInstaller = 1L << 30;
    private const int MaximumAnswer = 1 << 20;

    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private static readonly HttpClient Client = NewClient();

    private static HttpClient NewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("jGlancer", AboutWindow.Version));
        return client;
    }

    /// <summary>The latest release; null when none is published or the answer is unusable.</summary>
    public static async Task<AppRelease?> LatestAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CheckTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, AppUpdates.LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        return AppUpdates.ParseRelease(await ReadTextAsync(stream, timeout.Token).ConfigureAwait(false));
    }

    /// <summary>
    /// Downloads the installer and its signature into a new temporary folder and checks them. Throws
    /// <see cref="InvalidDataException"/> when the file is not ours; nothing of it is left then.
    /// </summary>
    public static async Task<VerifiedInstaller> DownloadAsync(AppRelease release, Action<long, long?> progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(release);
        var folder = Path.Combine(Path.GetTempPath(), FolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string signature;
            using (var answer = await Client.GetAsync(AppUpdates.SignatureUrl(release.VersionText), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                answer.EnsureSuccessStatusCode();
                await using var stream = await answer.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                signature = await ReadTextAsync(stream, cancellation).ConfigureAwait(false);
            }
            var path = Path.Combine(folder, AppUpdates.InstallerName(release.VersionText));
            using (var answer = await Client.GetAsync(AppUpdates.InstallerUrl(release.VersionText), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                answer.EnsureSuccessStatusCode();
                var total = answer.Content.Headers.ContentLength;
                await using var source = await answer.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                var received = 0L;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > MaximumInstaller)
                    {
                        throw new InvalidDataException("The installer is larger than any release.");
                    }
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
                    progress(received, total);
                }
            }
            // Checked and then started through one open handle's lifetime: nobody can write to the file in between.
            var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var hash = await SHA256.HashDataAsync(held, cancellation).ConfigureAwait(false);
                if (!AppUpdates.IsAuthentic(release.VersionText, hash, signature))
                {
                    throw new InvalidDataException("The signature does not match.");
                }
                return new VerifiedInstaller(path, held);
            }
            catch
            {
                await held.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            RemoveFolder(folder);
            throw;
        }
    }

    /// <summary>Starts the installer and ends the app, which the installer has to replace.</summary>
    public static void InstallAndQuit(VerifiedInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(installer);
        Process.Start(new ProcessStartInfo(installer.Path) { UseShellExecute = true })?.Dispose();
        installer.Dispose();
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>The look at start (Settings → General), at most once a day, for a version not skipped; failures stay silent.</summary>
    public static async Task CheckAtStartAsync(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        await Task.Delay(StartDelay).ConfigureAwait(true);
        _ = Task.Run(RemoveLeftovers);
        var store = SettingsStore.Current;
        if (!store.Settings.CheckForUpdates || !AppUpdates.IsCheckDue(store.Settings.LastUpdateCheck, DateTimeOffset.UtcNow))
        {
            return;
        }
        AppRelease? release;
        try
        {
            release = await LatestAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            return;
        }
        store.Update(s => s with { LastUpdateCheck = DateTimeOffset.UtcNow });
        if (release is not null && AppUpdates.IsNewer(release, AboutWindow.Version)
            && store.Settings.SkippedUpdateVersion != release.VersionText && window.IsVisible)
        {
            await UpdateWindow.ShowAsync(window, release).ConfigureAwait(true);
        }
    }

    /// <summary>Folders of earlier updates: the installer running from one is long done after a day.</summary>
    public static void RemoveLeftovers()
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(Path.GetTempPath(), FolderPrefix + "*"))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > TimeSpan.FromDays(1))
                {
                    RemoveFolder(folder);
                }
            }
        }
        catch (IOException)
        {
        }
    }

    public static void RemoveFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // In use: the next start takes it.
        }
    }

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken cancellation)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[1 << 14];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + count > MaximumAnswer)
            {
                throw new InvalidDataException("The answer is too long.");
            }
            memory.Write(buffer, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }
}
