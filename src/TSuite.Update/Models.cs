using System.Drawing;
using System.Text.Json.Serialization;

namespace TSuite.Update;

/// <summary>latest.json icerigi / contents of latest.json.</summary>
public sealed class LatestManifest
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("pub_date")] public string? PubDate { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
    /// <summary>Istege bagli: imzayi atan anahtarin kimligi (SPKI SHA-256 ilk 8 bayt, hex). Varsa yalniz o anahtar denenir.</summary>
    [JsonPropertyName("key_id")] public string? KeyId { get; set; }
    /// <summary>Istege bagli anahtar rotasyon kaydi (eski, guvenilir anahtarla imzali).</summary>
    [JsonPropertyName("key_rotation")] public KeyRotation? KeyRotation { get; set; }
}

/// <summary>Anahtar rotasyon kaydi; PROTOCOL.md "Anahtar rotasyonu".</summary>
public sealed class KeyRotation
{
    [JsonPropertyName("new_key")] public string NewKey { get; set; } = "";
    [JsonPropertyName("new_key_id")] public string NewKeyId { get; set; } = "";
    [JsonPropertyName("revoke_key_ids")] public List<string>? RevokeKeyIds { get; set; }
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
}

public sealed class AppUpdateState
{
    [JsonPropertyName("lastCheckUtc")] public DateTime LastCheckUtc { get; set; }
    [JsonPropertyName("skippedVersion")] public string? SkippedVersion { get; set; }
    /// <summary>"Sonra" denen bekleyen surum (ag olmadan gostermek icin; kurulum yine imzali manifeste dayanir).</summary>
    [JsonPropertyName("pending")] public PendingUpdate? Pending { get; set; }
    /// <summary>Rotasyonla ogrenilen kalici guvenilir anahtarlar (base64 SPKI).</summary>
    [JsonPropertyName("trustedKeys")] public List<string>? TrustedKeys { get; set; }
    /// <summary>Iptal edilen anahtar kimlikleri (gomulu anahtarlari da dusurur).</summary>
    [JsonPropertyName("revokedKeyIds")] public List<string>? RevokedKeyIds { get; set; }
}

public sealed class PendingUpdate
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("pub_date")] public string? PubDate { get; set; }
    [JsonPropertyName("savedUtc")] public DateTime SavedUtc { get; set; }
}

public sealed class UpdateStateFile
{
    [JsonPropertyName("apps")] public Dictionary<string, AppUpdateState> Apps { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LatestManifest))]
[JsonSerializable(typeof(UpdateStateFile))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext { }

public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>Uygulamanin kendi paletini tasiyan tema / per-app palette for the UpdateCard.</summary>
public sealed record UpdateTheme(
    Color Bg, Color Surface, Color Border, Color Accent, Color AccentHover, Color Text, Color TextDim)
{
    /// <summary>Accent uzerindeki yazi rengi (varsayilan beyaz).</summary>
    public Color OnAccent { get; init; } = Color.White;

    public static UpdateTheme Dark { get; } = new(
        Color.FromArgb(0x1E, 0x1E, 0x24), Color.FromArgb(0x2A, 0x2A, 0x32), Color.FromArgb(0x3A, 0x3A, 0x46),
        Color.FromArgb(0x4C, 0x8D, 0xFF), Color.FromArgb(0x6B, 0xA2, 0xFF), Color.FromArgb(0xEC, 0xEC, 0xF1), Color.FromArgb(0x9A, 0x9A, 0xA8));
}

/// <summary>Guncelleme adayi / update candidate.</summary>
public sealed record UpdateInfo(string CurrentVersion, string NewVersion, string Notes, string? PubDate, LatestManifest Manifest)
{
    /// <summary>Kalici bekleyen kayittan geldi (imza yok): indirmeden once manifest yeniden alinmali.</summary>
    public bool IsPending => string.IsNullOrEmpty(Manifest.Signature);
}

public enum UpdateState { Idle, Checking, Available, Downloading, Launching, Failed }
