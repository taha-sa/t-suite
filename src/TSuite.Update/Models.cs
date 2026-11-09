using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

[assembly: TypeForwardedTo(typeof(TSuite.Update.UpdateTheme))]

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
    [JsonPropertyName("signature2")] public string? Signature2 { get; set; }
    string? _minVersion, _channel;
    List<string>? _blockedVersions;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("min_version")] public string? MinVersion { get => _minVersion; set { _minVersion = value; HasSafetyFields = true; } }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blocked_versions")] public List<string>? BlockedVersions { get => _blockedVersions; set { _blockedVersions = value; HasSafetyFields = true; } }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel")] public string? Channel { get => _channel; set { _channel = value; HasSafetyFields = true; } }
    [JsonIgnore] public bool HasSafetyFields { get; private set; }
    public bool IsBlocked(string version) => BlockedVersions?.Any(v => VersionUtil.Compare(v, version) == 0) == true;
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
    [JsonPropertyName("channel")] public UpdateChannel? Channel { get; set; }
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

/// <summary>Guncelleme adayi / update candidate.</summary>
public sealed record UpdateInfo(string CurrentVersion, string NewVersion, string Notes, string? PubDate, LatestManifest Manifest)
{
    /// <summary>Kalici bekleyen kayittan geldi (imza yok): indirmeden once manifest yeniden alinmali.</summary>
    public bool IsRequired => !IsPending && (Manifest.IsBlocked(CurrentVersion) ||
        (Manifest.MinVersion != null && VersionUtil.Compare(CurrentVersion, Manifest.MinVersion) < 0));
    public bool IsPending => string.IsNullOrEmpty(Manifest.Signature);
}

public enum UpdateState { Idle, Checking, Available, Downloading, Launching, Failed }

[JsonConverter(typeof(JsonStringEnumConverter<UpdateChannel>))]
public enum UpdateChannel { Stable, Beta }
