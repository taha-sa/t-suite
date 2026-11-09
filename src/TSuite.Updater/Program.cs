using System.Diagnostics;
using System.IO.Compression;

// TSuite.Updater: ebeveyn surecin cikmasini bekler, exe'yi (veya zip icerigini) degistirir,
// eskiyi .bak'a yedekler, hata olursa geri alir, istenirse yeniden baslatir.
// Args: --pid N --target <exe> --source <yeni exe veya .zip> [--restart]

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "T-Suite", "logs");
Directory.CreateDirectory(logDir);
var logPath = Path.Combine(logDir, "updater.log");
void Log(string m)
{
    try { File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {m}{Environment.NewLine}"); } catch { }
}

int pid = 0; string? target = null, source = null; bool restart = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pid": if (i + 1 >= args.Length || !int.TryParse(args[++i], out pid)) return 2; break;
        case "--target": if (i + 1 >= args.Length) return 2; target = args[++i]; break;
        case "--source": if (i + 1 >= args.Length) return 2; source = args[++i]; break;
        case "--restart": restart = true; break;
    }
}
if (target == null || source == null) { Log("bad args"); return 2; }
Log($"start pid={pid} target={target} source={source}");

try
{
    if (pid > 0)
    {
        try { using var p = Process.GetProcessById(pid); if (!p.WaitForExit(60_000)) { Log("parent did not exit"); return 3; } }
        catch (ArgumentException) { /* zaten cikmis */ }
    }

    var replaced = new List<(string path, string bak)>();
    try
    {
        if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(target)!;
            using var zip = ZipFile.OpenRead(source);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // dizin
                var dest = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                if (!dest.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("zip path traversal: " + entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                Replace(dest, tmp => entry.ExtractToFile(tmp, true));
            }
        }
        else Replace(target, tmp => File.Copy(source, tmp, true));

        void Replace(string dest, Action<string> writeNew)
        {
            var tmp = dest + ".new";
            writeNew(tmp);
            var bak = dest + ".bak";
            bool had = File.Exists(dest);
            if (had) MoveWithRetry(dest, bak, true);
            try { MoveWithRetry(tmp, dest, false); }
            catch { if (had) File.Move(bak, dest, true); throw; }
            replaced.Add((dest, had ? bak : ""));
        }
    }
    catch (Exception ex)
    {
        Log("FAILED, rolling back: " + ex);
        foreach (var (path, bak) in replaced)
        {
            try { if (bak != "") File.Move(bak, path, true); else File.Delete(path); } catch (Exception e2) { Log("rollback error: " + e2.Message); }
        }
        if (restart) TryStart(target);
        return 1;
    }

    Log("replaced " + replaced.Count + " file(s)");
    try { File.Delete(source); } catch { }
    if (restart) TryStart(target);
    return 0;
}
catch (Exception ex) { Log("fatal: " + ex); return 1; }

static void MoveWithRetry(string from, string to, bool overwrite)
{
    for (int i = 0; ; i++)
    {
        try { File.Move(from, to, overwrite); return; }
        catch (IOException) when (i < 20) { Thread.Sleep(250); }
        catch (UnauthorizedAccessException) when (i < 20) { Thread.Sleep(250); }
    }
}

void TryStart(string exe)
{
    try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! }); Log("restarted"); }
    catch (Exception ex) { Log("restart failed: " + ex.Message); }
}
