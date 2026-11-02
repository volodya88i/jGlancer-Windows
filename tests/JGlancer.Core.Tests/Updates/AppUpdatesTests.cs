using System.Security.Cryptography;
using JGlancer.Core.Updates;

namespace JGlancer.Core.Tests.Updates;

public sealed class AppUpdatesTests
{
    [Theory]
    [InlineData("0.5.0", "0.5.0")]
    [InlineData("v1.20.3", "1.20.3")]
    public void AReleaseNamesItsVersion(string tag, string expected)
    {
        var release = AppUpdates.ParseRelease($$"""{"tag_name":"{{tag}}","draft":false,"prerelease":false,"body":" notes "}""");
        Assert.Equal(expected, release?.VersionText);
        Assert.Equal("notes", release?.Notes);
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.0.0","draft":true}""")]
    [InlineData("""{"tag_name":"v1.0.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("""{"tag_name":"v1.0.0/../../evil"}""")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsNoRelease(string? json) => Assert.Null(AppUpdates.ParseRelease(json));

    [Theory]
    [InlineData("0.5.1", "0.5.0", true)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("0.5.0", "0.5.0", false)]
    [InlineData("0.4.12", "0.5.0", false)]
    public void OnlyAHigherVersionIsNewer(string offered, string installed, bool newer)
    {
        var release = AppUpdates.ParseRelease($$"""{"tag_name":"{{offered}}"}""")!;
        Assert.Equal(newer, AppUpdates.IsNewer(release, installed));
    }

    [Fact]
    public void TheCheckRunsOnceADay()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.True(AppUpdates.IsCheckDue(null, now));
        Assert.False(AppUpdates.IsCheckDue(now.AddHours(-23), now));
        Assert.True(AppUpdates.IsCheckDue(now.AddHours(-24), now));
        Assert.True(AppUpdates.IsCheckDue(now.AddDays(3), now));
    }

    [Fact]
    public void DownloadsComeFromTheFixedRepository()
    {
        var url = AppUpdates.InstallerUrl("0.5.0").ToString();
        Assert.Equal("https://github.com/volodya88i/jGlancerWindows/releases/download/v0.5.0/jGlancer-Setup-0.5.0.exe", url);
        Assert.Equal(url + ".sig", AppUpdates.SignatureUrl("0.5.0").ToString());
    }

    [Fact]
    public void TheSignatureCoversTheFileAndTheVersion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var hash = SHA256.HashData([1, 2, 3]);
        var signature = Convert.ToBase64String(key.SignData(AppUpdates.SignedMessage("0.5.0", hash), HashAlgorithmName.SHA256));

        Assert.True(AppUpdates.IsAuthentic("0.5.0", hash, signature, publicKey));
        // An old signed installer offered under a new number.
        Assert.False(AppUpdates.IsAuthentic("0.6.0", hash, signature, publicKey));
        // A changed file.
        Assert.False(AppUpdates.IsAuthentic("0.5.0", SHA256.HashData([1, 2, 4]), signature, publicKey));
        // Someone else's key.
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(AppUpdates.IsAuthentic("0.5.0", hash, signature, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())));
        // Nonsense never throws.
        Assert.False(AppUpdates.IsAuthentic("0.5.0", hash, "not base64!", publicKey));
        Assert.False(AppUpdates.IsAuthentic("0.5.0", hash, null, publicKey));
    }

    [Fact]
    public void TheBuiltInKeyIsAPublicP256Key()
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(AppUpdates.PublicKey), out _);
        Assert.Equal(256, key.KeySize);
    }
}
