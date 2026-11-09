using System.Diagnostics;
using System.Text.Json;

namespace TSuite.Update;

public sealed class UpdateOptions
{
    /// <summary>Uygulama kimligi, ornegin "t-downloader".</summary>
    public required string AppId { get; init; }
    public required string CurrentVersion { get; init; }
    /// <summary>Base64 SPKI P-256 genel anahtar (uygulamaya gomulu).</summary>
    public required string PublicKeyBase64 { get; init; }
    /// <summary>Varsayilan: https://github.com/taha-sa/{AppId}-releases/releases/latest/download/latest.json</summary>
    public string? ManifestUrl { get; init; }
    public string? StatePath { get; init; }
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromHours(24);
    public HttpClient? Http { get; init; }
    /// <summary>Yalniz testler icin: https olmayan URL'lere izin ver.</summary>
    public bool AllowInsecureUrls { get; init; }
}

public sealed class UpdateChecker
{
    readonly UpdateOptions _o;
    readonly HttpClient _http;
    readonly string _statePath;
    static readonly object StateLock = new();

    public UpdateChecker(UpdateOptions o)
    {
        _o = o;
        _http = o.Http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _statePath = o.StatePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "T-Suite", "update-state.json");
    }

    public string ManifestUrl => _o.ManifestUrl ?? $"https://github.com/taha-sa/{_o.AppId}-releases/releases/latest/download/latest.json";

    UpdateStateFile LoadState()
    {
        try { return JsonSerializer.Deserialize(File.ReadAllBytes(_statePath), UpdateJsonContext.Default.UpdateStateFile) ?? new(); }
        catch { return new(); }
    }

    void SaveState(Action<AppUpdateState> mutate)
    {
        lock (StateLock)
        {
            try
            {
                var st = LoadState();
                if (!st.Apps.TryGetValue(_o.AppId, out var a)) st.Apps[_o.AppId] = a = new();
                mutate(a);
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                var tmp = _statePath + "." + Environment.ProcessId + ".tmp";
                File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(st, UpdateJsonContext.Default.UpdateStateFile));
                File.Move(tmp, _statePath, true);
            }
            catch { /* durum yazilamazsa sessizce gec */ }
        }
    }

    public DateTime LastCheckUtc => LoadState().Apps.TryGetValue(_o.AppId, out var a) ? a.LastCheckUtc : default;

    public void SkipVersion(string version) => SaveState(a => a.SkippedVersion = version);

    /// <summary>
    /// Guncelleme varsa UpdateInfo, yoksa (ya da 24 saat dolmadiysa / surum atlandiysa) null.
    /// force=true: aralik ve atlama yoksayilir. Ag hatasinda istisna firlatir.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        var st = LoadState().Apps.GetValueOrDefault(_o.AppId);
        if (!force && st != null && DateTime.UtcNow - st.LastCheckUtc < _o.MinInterval) return null;

        using var resp = await _http.GetAsync(ManifestUrl, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var m = JsonSerializer.Deserialize(bytes, UpdateJsonContext.Default.LatestManifest)
                ?? throw new InvalidDataException("empty latest.json");
        SaveState(a => a.LastCheckUtc = DateTime.UtcNow);

        if (string.IsNullOrEmpty(m.Version) || string.IsNullOrEmpty(m.Url)) throw new InvalidDataException("incomplete latest.json");
        if (!VersionUtil.IsNewer(_o.CurrentVersion, m.Version)) return null;
        if (!force && st?.SkippedVersion != null && VersionUtil.Compare(st.SkippedVersion, m.Version) == 0) return null;
        return new UpdateInfo(_o.CurrentVersion, m.Version, m.Notes, m.PubDate, m);
    }

    /// <summary>Gecici klasore indirir (ilerleme 0..1), SHA-256 + imzayi dogrular. Basarisizlikta dosya silinir.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var uri = new Uri(info.Manifest.Url);
        if (uri.Scheme != Uri.UriSchemeHttps && !_o.AllowInsecureUrls)
            throw new UpdateVerificationException("Download URL must be https");

        var dir = Path.Combine(Path.GetTempPath(), "TSuite-update", _o.AppId + "-" + info.NewVersion);
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(name)) name = _o.AppId + ".bin";
        var path = Path.Combine(dir, name);
        try
        {
            using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buf = new byte[81920]; long done = 0; int n; var sw = Stopwatch.StartNew(); long last = 0;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0 && sw.ElapsedMilliseconds - last >= 80) { last = sw.ElapsedMilliseconds; progress?.Report((double)done / total); }
                }
            }
            progress?.Report(1.0);
            UpdateSigning.VerifyFile(path, info.Manifest, _o.AppId, _o.PublicKeyBase64);
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Yardimciyi (TSuite.Updater.exe) gecici klasore kopyalayip calistirir; cagiran uygulama HEMEN cikmalidir.
    /// </summary>
    public static void LaunchUpdater(string downloadedFile, string? updaterExe = null, bool restart = true)
    {
        var targetExe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path");
        updaterExe ??= Path.Combine(AppContext.BaseDirectory, "TSuite.Updater.exe");
        if (!File.Exists(updaterExe)) throw new FileNotFoundException("TSuite.Updater.exe not found", updaterExe);
        var tmp = Path.Combine(Path.GetTempPath(), "TSuite-update", "updater-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        var copy = Path.Combine(tmp, "TSuite.Updater.exe");
        File.Copy(updaterExe, copy, true);
        // Cerceveye bagimli yardimci icin yan dosyalar (dll, runtimeconfig, deps) da kopyalanir.
        var srcDir = Path.GetDirectoryName(updaterExe)!;
        foreach (var f in Directory.EnumerateFiles(srcDir, "TSuite.Updater.*"))
            if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)), true);
        var psi = new ProcessStartInfo(copy) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--pid"); psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add("--target"); psi.ArgumentList.Add(targetExe);
        psi.ArgumentList.Add("--source"); psi.ArgumentList.Add(downloadedFile);
        if (restart) psi.ArgumentList.Add("--restart");
        Process.Start(psi);
    }
}
