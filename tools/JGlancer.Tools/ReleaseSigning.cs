using System.Security.Cryptography;
using JGlancer.Core.Updates;

namespace JGlancer.Tools;

/// <summary>Signs release installers for the update check (W-101). The private key stays outside the repository.</summary>
internal static class ReleaseSigning
{
    /// <summary>Writes a new private key to <paramref name="path"/>; returns the public key for <see cref="AppUpdates.PublicKey"/>.</summary>
    public static string CreateKey(string path)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Writes <c>installer.sig</c> beside the installer, after checking it against the app's built-in key.</summary>
    public static string Sign(string keyPath, string version, string installer)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyPath));
        byte[] hash;
        using (var file = File.OpenRead(installer))
        {
            hash = SHA256.HashData(file);
        }
        var signature = Convert.ToBase64String(key.SignData(AppUpdates.SignedMessage(version, hash), HashAlgorithmName.SHA256));
        if (!AppUpdates.IsAuthentic(version, hash, signature))
        {
            throw new InvalidOperationException("This key is not the one the app trusts.");
        }
        var output = installer + ".sig";
        File.WriteAllText(output, signature);
        return output;
    }
}
