using System.Text.Json;
using TSuite.Link;

namespace TSuite.Update;

/// <summary>
/// Standart güncelleme komutları (T-Bridge "Hepsini güncelle" için): uygulama isterse LinkServer'ına ekler.
///   app.update.check   -> { available, version }
///   app.update.install -> { started }  (indirme + kurulum başlar; UI'da UpdateCard/afiş ilerlemeyi gösterir)
/// </summary>
public static class UpdateLink
{
    public const string Check = "app.update.check";
    public const string Install = "app.update.install";
    public static readonly string[] Commands = [Check, Install];

    public static void Register(LinkServer server, UpdateController ctl)
    {
        server.OnCommand(Check, async (d, ct) =>
        {
            // Indirme/kurulum surerken yeniden kontrol etme: mevcut durumu raporla.
            if (ctl.State is UpdateState.Downloading or UpdateState.Launching)
                return LinkJson.Obj(("available", true), ("version", ctl.Info?.NewVersion ?? ""));
            bool avail = await ctl.CheckAsync(force: true, ct).ConfigureAwait(false);
            return LinkJson.Obj(("available", avail), ("version", ctl.Info?.NewVersion ?? ""));
        });
        server.OnCommand(Install, async (d, ct) =>
        {
            if (ctl.Info == null) await ctl.CheckAsync(force: true, ct).ConfigureAwait(false);
            if (ctl.Info == null) return LinkJson.Obj(("started", false));
            _ = ctl.StartUpdateAsync();
            return LinkJson.Obj(("started", true));
        });
    }
}

/// <summary>`--update` komut satırı: kontrol et, varsa indir/doğrula, yardımcıyı başlat (yeniden başlatma YOK) ve çık.</summary>
public static class UpdateCli
{
    public const string Flag = "--update";

    public static bool IsRequested(string[] args) => args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>AppRegistry'de kayitli, canli (pid>0) ve bu surec olmayan ornegin pid'i; yoksa bos liste.</summary>
    public static IReadOnlyList<int> FindRunningInstancePids(string appId, string? registryDir = null)
    {
        try
        {
            var m = new TSuite.Link.AppRegistry(registryDir).Read(appId);
            if (m != null && m.Pid > 0 && m.Pid != Environment.ProcessId && TSuite.Link.AppRegistry.IsAlive(m.Pid)) return [m.Pid];
        }
        catch { }
        return [];
    }

    /// <summary>0 = güncel veya güncelleme başlatıldı; 1 = hata. Çağıran süreç bundan sonra HEMEN çıkmalıdır.</summary>
    public static async Task<int> RunAsync(UpdateOptions options, bool launch = true)
    {
        try
        {
            var checker = new UpdateChecker(options);
            var info = await checker.CheckAsync(force: true).ConfigureAwait(false);
            if (info == null) return 0;
            var file = await checker.DownloadAsync(info).ConfigureAwait(false);
            if (launch)
            {
                // Calisan tepsi ornegi varsa onun cikisi da beklenir ve guncelleme sonrasi yeniden baslatilir.
                var running = FindRunningInstancePids(options.AppId);
                foreach (var pid in running)
                    try { using var p = System.Diagnostics.Process.GetProcessById(pid); p.CloseMainWindow(); } catch { }
                UpdateChecker.LaunchUpdater(file, restart: running.Count > 0, sha256: info.Manifest.Sha256, alsoWaitPids: running);
            }
            return 0;
        }
        catch { return 1; }
    }
}
