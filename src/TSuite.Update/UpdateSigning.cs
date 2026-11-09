using System.Security.Cryptography;
using System.Text;

namespace TSuite.Update;

/// <summary>
/// SHA-256 + ECDSA P-256 dogrulama. Imzalanan mesaj (UTF-8):
/// "tsuite-update-v1\n{appId}\n{version}\n{sha256-hex-kucuk}". Imza: IEEE P1363 (r||s), base64.
/// Surum ve uygulama kimligini imzaya baglamak, geri alma (downgrade) ve capraz-uygulama saldirilarini onler.
/// </summary>
public static class UpdateSigning
{
    public static byte[] Canonical(string appId, string version, string sha256Hex)
        => Encoding.UTF8.GetBytes($"tsuite-update-v1\n{appId}\n{version}\n{sha256Hex.ToLowerInvariant()}");

    public static string Sha256Hex(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    public static string Sign(ECDsa privateKey, string appId, string version, string sha256Hex)
        => Convert.ToBase64String(privateKey.SignData(Canonical(appId, version, sha256Hex), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public static bool VerifySignature(string publicKeyBase64, string appId, string version, string sha256Hex, string signatureBase64)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ec.VerifyData(Canonical(appId, version, sha256Hex), Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch { return false; }
    }

    /// <summary>Dosyanin hash'ini ve imzayi dogrular; uyusmazlikta UpdateVerificationException.</summary>
    public static void VerifyFile(string filePath, LatestManifest m, string appId, string publicKeyBase64)
    {
        var actual = Sha256Hex(filePath);
        if (!string.Equals(actual, m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateVerificationException("SHA-256 mismatch");
        if (!VerifySignature(publicKeyBase64, appId, m.Version, actual, m.Signature))
            throw new UpdateVerificationException("Invalid signature");
    }

    /// <summary>Yeni P-256 anahtar cifti: (PKCS#8 PEM ozel anahtar, base64 SPKI genel anahtar).</summary>
    public static (string PrivatePem, string PublicBase64) GenerateKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ec.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()));
    }
}
