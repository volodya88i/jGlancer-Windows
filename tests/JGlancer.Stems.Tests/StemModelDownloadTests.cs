using System.Net;
using System.Security.Cryptography;
using JGlancer.Core.Stems;
using JGlancer.Core.Tests.Support;
using JGlancer.Stems;

namespace JGlancer.Stems.Tests;

public sealed class StemModelDownloadTests
{
    /// <summary>Serves fixed bytes, optionally slowly in small pieces (so a stop can come in between).</summary>
    private sealed class Server(byte[] body, Action? onRead = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Trickle(body, onRead)) });
    }

    private sealed class Trickle(byte[] body, Action? onRead) : MemoryStream(body)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 1000)], cancellationToken);
        }
    }

    private static byte[] Model() => [.. Enumerable.Range(0, 10_000).Select(i => (byte)(i * 7))];

    private static StemModelSource Source(byte[] body, string? sha = null) =>
        new(new Uri("https://example.invalid/model.onnx"), sha ?? Convert.ToHexStringLower(SHA256.HashData(body)), body.Length);

    [Fact]
    public async Task AModelWithTheRightChecksumIsPutInPlace()
    {
        using var folder = new TemporaryDirectory();
        var body = Model();
        var target = StemModelDownload.PathIn(folder.Path, StemModelInfo.Four);
        var progress = new List<long>();
        using var client = new HttpClient(new Server(body));
        await StemModelDownload.DownloadAsync(client, Source(body), target, progress.Add, CancellationToken.None);
        Assert.Equal(body, await File.ReadAllBytesAsync(target));
        Assert.True(StemModelDownload.IsInstalled(folder.Path, StemModelInfo.Four));
        Assert.Equal(body.Length, progress[^1]);
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task AWrongChecksumLeavesNothingBehind()
    {
        using var folder = new TemporaryDirectory();
        var body = Model();
        var target = StemModelDownload.PathIn(folder.Path, StemModelInfo.Four);
        using var client = new HttpClient(new Server(body));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StemModelDownload.DownloadAsync(client, Source(body, new string('0', 64)), target, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(folder.Path));
        Assert.False(StemModelDownload.IsInstalled(folder.Path, StemModelInfo.Four));
    }

    [Fact]
    public async Task StoppingRemovesThePartialFile()
    {
        using var folder = new TemporaryDirectory();
        var body = Model();
        var target = StemModelDownload.PathIn(folder.Path, StemModelInfo.Six);
        using var stop = new CancellationTokenSource();
        var reads = 0;
        using var client = new HttpClient(new Server(body, () =>
        {
            if (++reads == 3)
            {
                stop.Cancel();
            }
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StemModelDownload.DownloadAsync(client, Source(body), target, null, stop.Token));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }
}
