using System.Security.Cryptography;
using JGlancer.Core.Stems;

namespace JGlancer.Stems;

/// <summary>Where a downloadable model comes from, and the checksum it must have.</summary>
public sealed record StemModelSource(Uri Address, string Sha256, long Bytes);

/// <summary>
/// The one network use of the app (W-85): a stem model, downloaded when the user presses
/// Download and never otherwise. It is written as "&lt;name&gt;.partial", checked against its SHA-256 and only then
/// renamed into place; a stop, an error or a wrong checksum removes the partial file.
/// </summary>
public static class StemModelDownload
{
    /// <summary>
    /// The model files (the public repository volodya88i/jGlancer-models): Demucs in release models-1
    /// (<c>scripts/stems/convert_demucs.py</c>), Mel-Band RoFormer in models-2 (<c>convert_roformer.py</c>), each then
    /// <c>half_weights.py</c> for the compact one (float16 weights, float32 compute: half the size).
    /// </summary>
    public static StemModelSource Source(StemModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (sha, bytes) = (model.Network, model.IsFull) switch
        {
            (StemNetwork.Vocals, true) => ("134b471a78cbd36755a8a48478ac34505adf98e8fbda26b9e164e53511f5a3a0", 918_412_650L),
            (StemNetwork.Vocals, false) => ("06ff6a79e0cfa8fb535062d82b48f8a20463e66dfdcac4853e623e783e3998d0", 461_981_015L),
            (StemNetwork.Six, true) => ("2f8d1e48cd6a4328f85f43afee3f73f53662450b3ecda0cb546e1508055d8a00", 114_560_305L),
            (StemNetwork.Six, false) => ("982cbe0b6a8e8715d2a142b9c5aae36d7d0c087b9c1382fab57d8e6d3aebfcd1", 59_959_882L),
            (_, true) => ("6941830d664bbf4a8238dbe9dec242b8c2799ce651a7dfbe7997b1e36b70c56f", 174_264_723L),
            _ => ("4782234fdd0ce38d94ae198746c680c898e1cb6c35d062c2042a3318871c113a", 90_555_550L),
        };
        // Demucs is in the first release, RoFormer in the second (W-120).
        var release = model.Network == StemNetwork.Vocals ? "models-2" : "models-1";
        return new(new Uri($"https://github.com/volodya88i/jGlancer-models/releases/download/{release}/" + model.FileName), sha, bytes);
    }

    /// <summary>The model's file in <paramref name="folder"/> (it may not exist yet).</summary>
    public static string PathIn(string folder, StemModelInfo model) => Path.Combine(folder, model.FileName);

    /// <summary>Whether the model is there (complete: only a checked download is ever renamed into place).</summary>
    public static bool IsInstalled(string folder, StemModelInfo model) => File.Exists(PathIn(folder, model));

    /// <summary>
    /// Downloads the model into <paramref name="folder"/>; <paramref name="progress"/> gets the bytes received.
    /// Throws <see cref="InvalidDataException"/> when the checksum does not match.
    /// </summary>
    public static async Task DownloadAsync(HttpClient client, StemModelSource source, string target, Action<long>? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var partial = target + ".partial";
        try
        {
            using (var response = await client.GetAsync(source.Address, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                await CopyAsync(body, file, progress, cancellation).ConfigureAwait(false);
            }
            if (!string.Equals(await HashAsync(partial, cancellation).ConfigureAwait(false), source.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded model is damaged (checksum mismatch).");
            }
            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>Copies with progress (bytes so far); used for the download and testable on its own.</summary>
    public static async Task CopyAsync(Stream from, Stream to, Action<long>? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            total += read;
            progress?.Invoke(total);
        }
    }

    /// <summary>The SHA-256 of a file, lower-case hex.</summary>
    public static async Task<string> HashAsync(string path, CancellationToken cancellation)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellation).ConfigureAwait(false));
    }
}
