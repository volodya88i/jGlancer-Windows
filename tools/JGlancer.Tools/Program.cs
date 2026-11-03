using System.Globalization;
using JGlancer.Tools;

// Usage (from the repository root):
//   dotnet run --project tools/JGlancer.Tools -- tokens
//   dotnet run --project tools/JGlancer.Tools -- strings [path to Localizable.xcstrings]
//   dotnet run --project tools/JGlancer.Tools -- icons <path to lucide-static package>
//   dotnet run --project tools/JGlancer.Tools -- update-key <private key file, outside the repository>
//   dotnet run --project tools/JGlancer.Tools -- update-sign <private key file> <version> <installer>
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var root = RepoPaths.FindRoot();
var command = args.Length > 0 ? args[0] : "";
switch (command)
{
    case "tokens":
        Write(RepoPaths.TokensOutput(root), TokenGenerator.Generate(File.ReadAllText(RepoPaths.TokensInput(root))));
        break;
    case "strings":
        var source = args.Length > 1 ? args[1] : RepoPaths.DefaultXcstrings(root);
        foreach (var (language, json) in StringsGenerator.Generate(File.ReadAllText(source)))
        {
            Write(RepoPaths.StringsOutput(root, language), json);
        }
        break;
    case "icons":
        if (args.Length < 2)
        {
            return Fail("icons needs the path to an unpacked lucide-static package");
        }
        var names = File.ReadAllLines(RepoPaths.IconListInput(root));
        Write(RepoPaths.IconsOutput(root), IconGenerator.Generate(File.ReadAllText(Path.Combine(args[1], "icon-nodes.json")), names));
        break;
    case "update-key":
        if (args.Length < 2 || File.Exists(args[1]))
        {
            return Fail("update-key needs a new file; an existing key is never replaced: installed apps trust it");
        }
        Console.WriteLine(ReleaseSigning.CreateKey(args[1]));
        break;
    case "update-sign":
        if (args.Length < 4)
        {
            return Fail("update-sign needs the key file, the version and the installer");
        }
        Console.WriteLine(ReleaseSigning.Sign(args[1], args[2], args[3]));
        break;
    case "metadump" when OperatingSystem.IsWindows():
        foreach (var file in args.Skip(1))
        {
            Console.Write(JGlancer.Platform.Windows.MetadataDump.Describe(Path.GetFullPath(file)));
        }
        break;
    case "synth" when OperatingSystem.IsWindows():
        Bench.Synthesize(int.Parse(args[1], CultureInfo.InvariantCulture), args.Contains("--media"), Path.Combine(root, "TestMedia", "fixtures"));
        break;
    case "segment" when OperatingSystem.IsWindows():
        // A segment as the app saves it: segment <in> <start> <end> <out> [exact].
        JGlancer.Platform.Windows.SegmentExporter.ExportAsync(new JGlancer.Core.Playback.SegmentExportRequest(
                args[1], JGlancer.Core.Model.MediaKind.Video,
                new JGlancer.Core.Playback.SegmentRange(double.Parse(args[2], CultureInfo.InvariantCulture), double.Parse(args[3], CultureInfo.InvariantCulture)),
                ExactVideo: args.Contains("exact")),
            args[4], CancellationToken.None).GetAwaiter().GetResult();
        break;
    case "keylab":
        return JGlancer.Tools.KeyLab.Run(args.Skip(1));
    case "analyze":
        // Tempo and key of songs, as the player finds them: analyze <files> (reads them only, plays nothing).
        foreach (var song in args.Skip(1))
        {
            var samples = JGlancer.Mpv.MpvAudioDecoder.SamplesAsync(song, CancellationToken.None).GetAwaiter().GetResult() ?? [];
            var rate = JGlancer.Mpv.MpvAudioDecoder.SamplesPerSecond;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var bpm = JGlancer.Core.Audio.TempoDetector.Detect(samples, rate);
            var key = JGlancer.Core.Audio.KeyDetector.Detect(samples, rate);
            Console.WriteLine($"{Path.GetFileName(song)}: {bpm:0.0} BPM, {key?.Label}, {samples.Length / (double)rate:0}s of audio in {clock.ElapsedMilliseconds} ms");
        }
        break;
    case "mpvencode":
        return MpvEncode.Run(args[1], args[2], args.Skip(3));
    case "openwith" when OperatingSystem.IsWindows():
        // What the shell recommends for a file type (Open With): name, display name, icon, default.
        foreach (var handler in JGlancer.Platform.Windows.ShellOpenWith.Recommended(args[1]))
        {
            Console.WriteLine($"{(handler.IsDefault ? "*" : " ")} {handler.DisplayName} | {handler.Name} | {handler.IconPath}");
            handler.Dispose();
        }
        break;
    case "wicmodes" when OperatingSystem.IsWindows():
        ThumbBench.Modes(args[1]);
        break;
    case "thumbbench" when OperatingSystem.IsWindows():
        ThumbBench.Run(args[1], args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20);
        break;
    case "synth-photos":
        SyntheticPhotos.Create(int.Parse(args[1], CultureInfo.InvariantCulture));
        break;
    case "stems":
        return await StemBench.RunAsync(args[1], args[2], args[3], args[4], args.Contains("--cpu"));
    case "scan" when OperatingSystem.IsWindows():
        return await Bench.ScanAsync(Path.GetFullPath(args[1]), args.Contains("--metadata"));
    default:
        return Fail("commands: tokens | strings [xcstrings] | icons <lucide-static dir> | metadump <files…> | synth <N> [--media] | scan <folder> [--metadata]");
}
return 0;

static void Write(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    Console.WriteLine($"wrote {Path.GetRelativePath(Environment.CurrentDirectory, path)}");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
