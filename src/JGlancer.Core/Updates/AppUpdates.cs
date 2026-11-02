using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JGlancer.Core.Updates;

/// <summary>A published version of the app, as the update check understands it.</summary>
public sealed record AppRelease(Version Version, string VersionText, string Notes);

/// <summary>
/// The rules of the update check (W-101): where releases live, which answers count as a newer version, and whether a
/// download really is one of ours. An installer runs only when its signature (ECDSA P-256, the private key is never in
/// the repository) covers both its SHA-256 and the version offered, so neither a changed file nor an old signed
/// installer under a new number gets through, even from a compromised account.
/// </summary>
public static partial class AppUpdates
{
    public const string ProfileUrl = "https://github.com/volodya88i";

    public const string RepositoryUrl = ProfileUrl + "/jGlancerWindows";

    public const string LatestReleaseUrl = "https://api.github.com/repos/volodya88i/jGlancerWindows/releases/latest";

    /// <summary>The public half of the release signing key (SubjectPublicKeyInfo, Base64).</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEL64rMagrInxEs1HrIrhJWAGJ8gQ/7zAKYyxEnvN5bpwob+vOwh7aJMrpLJfbW/itDiawa8NdJGW7v2BgnNAUVw==";

    /// <summary>How long release notes may be when shown.</summary>
    private const int MaximumNotes = 4000;

    /// <summary>How often the app looks by itself.</summary>
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromDays(1);

    public static string InstallerName(string version) => $"jGlancer-Setup-{version}.exe";

    /// <summary>
    /// Built from the fixed repository, never taken from the answer of the release API.
    /// </summary>
    public static Uri InstallerUrl(string version) => new($"{RepositoryUrl}/releases/download/v{version}/{InstallerName(version)}");

    public static Uri SignatureUrl(string version) => new($"{InstallerUrl(version)}.sig");

    /// <summary>The release in an answer of the release API; null for a draft, a pre-release or anything unexpected.</summary>
    public static AppRelease? ParseRelease(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                || (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                || ParseVersion(tag.GetString()) is not { } version)
            {
                return null;
            }
            var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
            notes = notes.Trim();
            return new AppRelease(version, version.ToString(3), notes.Length > MaximumNotes ? notes[..MaximumNotes] + "…" : notes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"0.5.0" or "v0.5.0"; null for anything else, so nothing odd ever reaches a download address.</summary>
    public static Version? ParseVersion(string? text) =>
        text is not null && VersionPattern().Match(text) is { Success: true } match
            ? new Version(Number(match, 1), Number(match, 2), Number(match, 3))
            : null;

    public static bool IsNewer(AppRelease release, string current)
    {
        ArgumentNullException.ThrowIfNull(release);
        return ParseVersion(current) is { } installed && release.Version > installed;
    }

    /// <summary>True on the first run, after the interval, and when the saved time is in the future (a changed clock).</summary>
    public static bool IsCheckDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is not { } previous || previous > now || now - previous >= CheckInterval;

    /// <summary>What the signature covers.</summary>
    public static byte[] SignedMessage(string version, ReadOnlySpan<byte> sha256) =>
        Encoding.UTF8.GetBytes($"jGlancer-update\n{version}\n{Convert.ToHexStringLower(sha256)}");

    /// <summary>Whether <paramref name="signature"/> (Base64) is ours for this version and file hash.</summary>
    public static bool IsAuthentic(string version, ReadOnlySpan<byte> sha256, string? signature, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(SignedMessage(version, sha256), Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^v?(\d{1,4})\.(\d{1,4})\.(\d{1,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
