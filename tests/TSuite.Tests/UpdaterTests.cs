using System.Diagnostics;
using System.Security.Cryptography;

namespace TSuite.Tests;

public class UpdaterTests
{
    static int RunUpdater(params string[] args)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "TSuite.Updater.dll");
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add(dll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        Assert.True(p.WaitForExit(60_000));
        return p.ExitCode;
    }

    static (string dir, string target, string source, string sha) Make()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tsuite-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "app.exe"); var source = Path.Combine(dir, "new.bin");
        File.WriteAllText(target, "OLD"); File.WriteAllText(source, "NEW");
        return (dir, target, source, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
    }

    [Fact]
    public void Source_modified_after_verification_is_rejected_and_target_unchanged()
    {
        var (_, target, source, sha) = Make();
        File.WriteAllText(source, "EVIL"); // dogrulamadan sonra degistirildi
        Assert.Equal(3, RunUpdater("--target", target, "--source", source, "--sha256", sha));
        Assert.Equal("OLD", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".bak"));
    }

    [Fact]
    public void Matching_sha_replaces_and_missing_sha_keeps_old_behavior()
    {
        var (_, target, source, sha) = Make();
        Assert.Equal(0, RunUpdater("--target", target, "--source", source, "--sha256", sha.ToUpperInvariant()));
        Assert.Equal("NEW", File.ReadAllText(target));
        var (_, t2, s2, _) = Make();
        Assert.Equal(0, RunUpdater("--target", t2, "--source", s2));
        Assert.Equal("NEW", File.ReadAllText(t2));
    }

    [Fact]
    public void Waits_for_every_pid_before_replacing()
    {
        var (_, target, source, sha) = Make();
        var sleeper = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -Command Start-Sleep -Seconds 4") { CreateNoWindow = true, UseShellExecute = false })!;
        var sw = Stopwatch.StartNew();
        Assert.Equal(0, RunUpdater("--pid", "999999", "--pid", sleeper.Id.ToString(), "--target", target, "--source", source, "--sha256", sha));
        Assert.True(sleeper.HasExited, "updater must not replace before the second pid exits");
        Assert.Equal("NEW", File.ReadAllText(target));
        sleeper.Dispose();
    }

    [Fact]
    public void FindRunningInstancePids_uses_registry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tsuite-reg-" + Guid.NewGuid().ToString("N"));
        var reg = new TSuite.Link.AppRegistry(dir);
        var sleeper = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -Command Start-Sleep -Seconds 5") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.Empty(TSuite.Update.UpdateCli.FindRunningInstancePids("appx", dir));
            reg.Write(new TSuite.Link.AppManifest { Id = "appx", Pid = sleeper.Id, ExePath = "x" });
            Assert.Equal([sleeper.Id], TSuite.Update.UpdateCli.FindRunningInstancePids("appx", dir));
            reg.Write(new TSuite.Link.AppManifest { Id = "appx", Pid = 0, ExePath = "x" });
            Assert.Empty(TSuite.Update.UpdateCli.FindRunningInstancePids("appx", dir));
        }
        finally { sleeper.Kill(); sleeper.Dispose(); }
    }
}
