namespace JGlancer.Mpv;

/// <summary>
/// libmpv's encoding mode: a headless instance plays the input through its filters into the output file as fast as it
/// can (segment export, W-40). Options are mpv's (<c>o</c>, <c>of</c>, <c>oac</c>, <c>oacopts</c>, <c>start</c>,
/// <c>end</c>, <c>af</c>, …), set before the instance starts as encoding requires.
/// </summary>
public static class MpvEncoder
{
    /// <summary>True when the input played to its end (or <c>end</c>) and the output was written.</summary>
    public static async Task<bool> EncodeAsync(string input, string output, IEnumerable<(string Name, string Value)> options, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(options);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new MpvPlayer(
            new (string Name, string Value)[] { ("o", output), ("keep-open", "no"), ("vid", "auto") }.Concat(options),
            withVideo: true);
        player.Ended += reason => finished.TrySetResult(reason == MpvEndReason.EndOfFile);
        using var registration = cancellation.Register(() => finished.TrySetResult(false));
        player.Load(input);
        return await finished.Task.ConfigureAwait(false) && File.Exists(output);
    }
}
