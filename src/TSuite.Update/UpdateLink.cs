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

    /// <summary>0 = güncel veya güncelleme başlatıldı; 1 = hata. Çağıran süreç bundan sonra HEMEN çıkmalıdır.</summary>
    public static async Task<int> RunAsync(UpdateOptions options, bool launch = true)
    {
        try
        {
            var checker = new UpdateChecker(options);
            var info = await checker.CheckAsync(force: true).ConfigureAwait(false);
            if (info == null) return 0;
            var file = await checker.DownloadAsync(info).ConfigureAwait(false);
            if (launch) UpdateChecker.LaunchUpdater(file, restart: false);
            return 0;
        }
        catch { return 1; }
    }
}
