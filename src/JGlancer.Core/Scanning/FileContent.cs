namespace JGlancer.Core.Scanning;

/// <summary>Whole-file comparison: the proof before a duplicate goes to the Recycle Bin (W-78). Only reads.</summary>
public static class FileContent
{
    private const int ChunkSize = 1024 * 1024;

    /// <summary>
    /// True when both files hold exactly the same bytes; false when they differ, either cannot be read, or they are the
    /// same file. Stops at the first difference.
    /// </summary>
    public static async Task<bool> SameAsync(string first, string second, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(first);
        ArgumentException.ThrowIfNullOrEmpty(second);
        if (string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete, Options = FileOptions.SequentialScan | FileOptions.Asynchronous, BufferSize = 0 };
            await using var a = new FileStream(first, options);
            await using var b = new FileStream(second, options);
            if (a.Length != b.Length)
            {
                return false;
            }
            var left = new byte[ChunkSize];
            var right = new byte[ChunkSize];
            while (true)
            {
                var read = await ReadFullyAsync(a, left, cancellation).ConfigureAwait(false);
                var other = await ReadFullyAsync(b, right, cancellation).ConfigureAwait(false);
                if (read != other || !left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, other)))
                {
                    return false;
                }
                if (read == 0)
                {
                    return true;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellation)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellation).ConfigureAwait(false)) > 0)
        {
            total += read;
        }
        return total;
    }
}
