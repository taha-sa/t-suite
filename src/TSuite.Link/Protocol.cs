using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSuite.Link;

/// <summary>Protokol sabitleri / Protocol constants.</summary>
public static class LinkProtocol
{
    public const int Version = 1;
    public const string PipePrefix = "TSuite.";
    public const int MaxLineBytes = 1 << 20;

    public const string TypeEvent = "event", TypeCmd = "cmd", TypeReply = "reply", TypeHello = "hello", TypeSubscribe = "subscribe";

    public static string PipeName(string appId) => PipePrefix + appId;
}

/// <summary>Tek satirlik JSON zarfi / one-line JSON envelope.</summary>
public sealed class Envelope
{
    [JsonPropertyName("v")] public int V { get; set; } = LinkProtocol.Version;
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }
}

/// <summary>Kayit defteri manifesti / registry manifest.</summary>
public sealed class AppManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("exePath")] public string ExePath { get; set; } = "";
    [JsonPropertyName("pipeName")] public string PipeName { get; set; } = "";
    [JsonPropertyName("events")] public string[] Events { get; set; } = [];
    [JsonPropertyName("commands")] public string[] Commands { get; set; } = [];
    [JsonPropertyName("releasesRepo")] public string? ReleasesRepo { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    /// <summary>Calisan surecin PID'i (0 = calismiyor) / running process id.</summary>
    [JsonPropertyName("pid")] public int Pid { get; set; }
}

public sealed class SubscribeData
{
    [JsonPropertyName("prefixes")] public string[] Prefixes { get; set; } = [];
}

public sealed class HelloData
{
    [JsonPropertyName("appId")] public string AppId { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("protocol")] public int Protocol { get; set; } = LinkProtocol.Version;
    [JsonPropertyName("events")] public string[] Events { get; set; } = [];
    [JsonPropertyName("commands")] public string[] Commands { get; set; } = [];
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(SubscribeData))]
[JsonSerializable(typeof(HelloData))]
internal sealed partial class LinkJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(AppManifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext { }

/// <summary>Yansimasiz (reflection-free) kucuk JSON yardimcilari.</summary>
public static class LinkJson
{
    /// <summary>{"a":1,"b":"x"} uretir. Desteklenen: null,string,bool,int,long,double,JsonElement.</summary>
    public static JsonElement Obj(params (string Key, object? Value)[] fields)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (k, v) in fields)
            {
                w.WritePropertyName(k);
                switch (v)
                {
                    case null: w.WriteNullValue(); break;
                    case string s: w.WriteStringValue(s); break;
                    case bool b: w.WriteBooleanValue(b); break;
                    case int i: w.WriteNumberValue(i); break;
                    case long l: w.WriteNumberValue(l); break;
                    case double d: w.WriteNumberValue(d); break;
                    case JsonElement e: e.WriteTo(w); break;
                    default: throw new ArgumentException($"Unsupported value type {v.GetType()}");
                }
            }
            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    public static JsonElement Parse(string json)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.Clone();
    }

    internal static byte[] Serialize(Envelope e)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(e, LinkJsonContext.Default.Envelope);
        var buf = new byte[json.Length + 1];
        json.CopyTo(buf, 0);
        buf[^1] = (byte)'\n';
        return buf;
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
