using System.Text.Json;

namespace TSuite.Link;

/// <summary>%LOCALAPPDATA%\T-Suite\apps\&lt;id&gt;.json kayit defteri.</summary>
public sealed class AppRegistry
{
    public string Directory { get; }

    public AppRegistry(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "T-Suite", "apps");
    }

    static void CheckId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Invalid app id (use [A-Za-z0-9_-]): " + id);
    }

    string PathFor(string id) { CheckId(id); return Path.Combine(Directory, id + ".json"); }

    /// <summary>Atomik yazim (tmp + move).</summary>
    public void Write(AppManifest m)
    {
        var path = PathFor(m.Id);
        if (string.IsNullOrEmpty(m.PipeName)) m.PipeName = LinkProtocol.PipeName(m.Id);
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(m, ManifestJsonContext.Default.AppManifest));
        File.Move(tmp, path, overwrite: true);
    }

    public AppManifest? Read(string id)
    {
        try { return ReadFile(PathFor(id)); } catch { return null; }
    }

    static AppManifest? ReadFile(string path)
    {
        try { return JsonSerializer.Deserialize(File.ReadAllBytes(path), ManifestJsonContext.Default.AppManifest); }
        catch { return null; }
    }

    public IReadOnlyList<AppManifest> ReadAll()
    {
        var list = new List<AppManifest>();
        if (!System.IO.Directory.Exists(Directory)) return list;
        foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var m = ReadFile(f);
            if (m != null && !string.IsNullOrEmpty(m.Id)) list.Add(m);
        }
        return list;
    }

    public void Remove(string id)
    {
        try { File.Delete(PathFor(id)); } catch { }
    }

    /// <summary>
    /// Bayat kayitlari temizler: exePath artik yoksa (kaldirilmis uygulama) dosya silinir;
    /// pid olu ise pid 0'a cekilir. Silinen id'leri dondurur.
    /// </summary>
    public IReadOnlyList<string> CleanupStale()
    {
        var removed = new List<string>();
        if (!System.IO.Directory.Exists(Directory)) return removed;
        foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var m = ReadFile(f);
            if (m == null) { try { File.Delete(f); } catch { } continue; }
            if (string.IsNullOrEmpty(m.ExePath) || !File.Exists(m.ExePath))
            {
                try { File.Delete(f); removed.Add(m.Id); } catch { }
            }
            else if (m.Pid != 0 && !IsAlive(m.Pid))
            {
                m.Pid = 0;
                try { Write(m); } catch { }
            }
        }
        return removed;
    }

    public static bool IsAlive(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }
}
