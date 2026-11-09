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

    public static byte[] CanonicalV2(string appId, LatestManifest m)
    {
        if (!UpdateChecker.IsValidVersion(m.Version) ||
            (m.MinVersion != null && !UpdateChecker.IsValidVersion(m.MinVersion)) ||
            (m.BlockedVersions != null && m.BlockedVersions.Any(v => !UpdateChecker.IsValidVersion(v))) ||
            (m.Channel != null && m.Channel is not ("stable" or "beta")))
            throw new UpdateVerificationException("Invalid update safety fields");
        return Encoding.UTF8.GetBytes($"tsuite-update-v2\n{appId}\n{m.Version}\n{m.Sha256.ToLowerInvariant()}\n{m.MinVersion}\n{string.Join(",", m.BlockedVersions ?? [])}\n{m.Channel}");
    }

    public static string SignV2(ECDsa key, string appId, LatestManifest m)
        => Convert.ToBase64String(key.SignData(CanonicalV2(appId, m), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public static bool VerifySignatureV2(string key, string appId, LatestManifest m)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            return ec.VerifyData(CanonicalV2(appId, m), Convert.FromBase64String(m.Signature2 ?? ""),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch { return false; }
    }

    public static void VerifySafetyFields(IEnumerable<string> keys, string appId, LatestManifest m)
    {
        if (!m.HasSafetyFields) return;
        if (!keys.Any(k => (string.IsNullOrEmpty(m.KeyId) || string.Equals(KeyId(k), m.KeyId, StringComparison.OrdinalIgnoreCase))
            && VerifySignature(k, appId, m.Version, m.Sha256, m.Signature) && VerifySignatureV2(k, appId, m)))
            throw new UpdateVerificationException("Invalid signature2");
    }

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

    /// <summary>Anahtar kimligi: SPKI baytlarinin SHA-256 ilk 8 baytinin kucuk harf hex'i; gecersizse "".</summary>
    public static string KeyId(string publicKeyBase64)
    {
        try { return Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(publicKeyBase64))[..8]); }
        catch { return ""; }
    }

    /// <summary>Birden cok guvenilir anahtardan HERHANGI biriyle dogrular; keyId verilirse yalniz eslesen anahtar denenir.</summary>
    public static bool VerifySignature(IEnumerable<string> trustedKeys, string? keyId, string appId, string version, string sha256Hex, string signatureBase64)
    {
        foreach (var k in trustedKeys)
        {
            if (!string.IsNullOrEmpty(keyId) && !string.Equals(KeyId(k), keyId, StringComparison.OrdinalIgnoreCase)) continue;
            if (VerifySignature(k, appId, version, sha256Hex, signatureBase64)) return true;
        }
        return false;
    }

    public static byte[] RotationCanonical(string appId, string newKeyId, string newKeyBase64, IEnumerable<string>? revokeIds)
        => Encoding.UTF8.GetBytes($"tsuite-keyrot-v1\n{appId}\n{newKeyId}\n{newKeyBase64}\n{string.Join(",", revokeIds ?? [])}");

    public static KeyRotation SignRotation(ECDsa oldPrivateKey, string appId, string newKeyBase64, IEnumerable<string>? revokeIds = null)
    {
        var ids = revokeIds?.ToList() ?? [];
        var nid = KeyId(newKeyBase64);
        var sig = Convert.ToBase64String(oldPrivateKey.SignData(RotationCanonical(appId, nid, newKeyBase64, ids),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return new KeyRotation { NewKey = newKeyBase64, NewKeyId = nid, RevokeKeyIds = ids.Count > 0 ? ids : null, Signature = sig };
    }

    /// <summary>Rotasyon kaydi, guvenilir anahtarlardan biriyle imzali ve new_key_id tutarli mi?</summary>
    public static bool VerifyRotation(IEnumerable<string> trustedKeys, string appId, KeyRotation r)
    {
        if (string.IsNullOrEmpty(r.NewKey) || !string.Equals(KeyId(r.NewKey), r.NewKeyId, StringComparison.OrdinalIgnoreCase)) return false;
        var msg = RotationCanonical(appId, r.NewKeyId, r.NewKey, r.RevokeKeyIds);
        foreach (var k in trustedKeys)
        {
            try
            {
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k), out _);
                if (ec.VerifyData(msg, Convert.FromBase64String(r.Signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>Dosyanin hash'ini ve imzayi dogrular; uyusmazlikta UpdateVerificationException.</summary>
    public static void VerifyFile(string filePath, LatestManifest m, string appId, string publicKeyBase64)
    {
        VerifySafetyFields([publicKeyBase64], appId, m);
        if (m.IsBlocked(m.Version)) throw new UpdateVerificationException("Offered version is blocked");
        var actual = Sha256Hex(filePath);
        if (!string.Equals(actual, m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateVerificationException("SHA-256 mismatch");
        if (!VerifySignature(publicKeyBase64, appId, m.Version, actual, m.Signature))
            throw new UpdateVerificationException("Invalid signature");
    }

    /// <summary>Coklu anahtar surumu; m.KeyId varsa yalniz o anahtar denenir.</summary>
    public static void VerifyFile(string filePath, LatestManifest m, string appId, IReadOnlyList<string> trustedKeys)
    {
        VerifySafetyFields(trustedKeys, appId, m);
        if (m.IsBlocked(m.Version)) throw new UpdateVerificationException("Offered version is blocked");
        var actual = Sha256Hex(filePath);
        if (!string.Equals(actual, m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateVerificationException("SHA-256 mismatch");
        if (!VerifySignature(trustedKeys, m.KeyId, appId, m.Version, actual, m.Signature))
            throw new UpdateVerificationException("Invalid signature");
    }

    /// <summary>Yeni P-256 anahtar cifti: (PKCS#8 PEM ozel anahtar, base64 SPKI genel anahtar).</summary>
    public static (string PrivatePem, string PublicBase64) GenerateKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ec.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()));
    }
}
