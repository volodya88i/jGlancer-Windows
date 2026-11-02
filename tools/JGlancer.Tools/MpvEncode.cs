using System.Diagnostics;
using JGlancer.Mpv;

namespace JGlancer.Tools;

/// <summary>
/// <c>mpvencode &lt;in&gt; &lt;out&gt; name=value…</c>: runs libmpv's encoding mode with the given options (experiments for
/// segment export; reads the input, writes only the output).
/// </summary>
internal static class MpvEncode
{
    public static int Run(string input, string output, IEnumerable<string> options)
    {
        var parsed = options.Select(option => option.Split('=', 2)).Select(parts => (parts[0], parts.Length > 1 ? parts[1] : "")).ToList();
        var clock = Stopwatch.StartNew();
        var result = MpvEncoder.EncodeAsync(input, output, parsed, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"{result} in {clock.Elapsed.TotalMilliseconds:0} ms; output {(File.Exists(output) ? new FileInfo(output).Length : 0)} bytes");
        return result ? 0 : 1;
    }
}
