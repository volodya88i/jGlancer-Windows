using System.Buffers.Binary;
using System.Security.Cryptography;
using JGlancer.Core.Store;

namespace JGlancer.Core.Scanning;

/// <summary>
/// A cheap content fingerprint for identity matching (macOS D-31): SHA-256 over the size and the first and last
/// 64 KB, truncated to 16 bytes. The middle is not sampled (documented limitation).
/// </summary>
public static class QuickHash
{
    /// <summary>Bytes read from each end of the file.</summary>
    public const int SampleSize = 64 * 1024;

    /// <summary>The fingerprint of the file at <paramref name="path"/>, or null if it cannot be read.</summary>
    public static byte[]? Compute(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            var size = stream.Length;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> sizeBytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(sizeBytes, (ulong)size);
            hash.AppendData(sizeBytes);
            var buffer = new byte[SampleSize];
            hash.AppendData(buffer, 0, ReadFully(stream, buffer));
            if (size > SampleSize)
            {
                stream.Position = Math.Max(SampleSize, size - SampleSize);
                hash.AppendData(buffer, 0, ReadFully(stream, buffer));
            }
            return hash.GetHashAndReset()[..16];
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }
        return total;
    }
}

/// <summary>The real file system, as seen by the store's identity matching.</summary>
public sealed class DiskFileProbe : IFileProbe
{
    public bool FileExists(string path) => File.Exists(path);

    public byte[]? QuickHash(string path) => Scanning.QuickHash.Compute(path);
}
