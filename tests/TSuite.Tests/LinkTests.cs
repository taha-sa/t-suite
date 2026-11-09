using System.Text.Json;
using TSuite.Link;

namespace TSuite.Tests;

public class LinkTests
{
    static string NewId() => "test" + Guid.NewGuid().ToString("N")[..10];

    static async Task<(LinkServer, LinkClient)> Pair(string id, params string[] prefixes)
    {
        var srv = new LinkServer(id, "1.0.0", ["download.progress", "ram.status"], ["ram.clean"]);
        srv.OnCommand("ram.clean", (d, ct) => Task.FromResult<JsonElement?>(LinkJson.Obj(("freedMb", 42))));
        srv.OnCommand("boom", (d, ct) => throw new InvalidOperationException("kaboom"));
        srv.Start();
        var cli = new LinkClient(id, TimeSpan.FromMilliseconds(100));
        cli.Start(prefixes);
        Assert.True(await cli.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
        return (srv, cli);
    }

    [Fact]
    public async Task Command_round_trip_and_hello()
    {
        var id = NewId();
        var (srv, cli) = await Pair(id);
        await using var _s = srv; await using var _c = cli;
        Assert.Equal(id, cli.Hello!.AppId);
        var r = await cli.SendCommandAsync("ram.clean", timeout: TimeSpan.FromSeconds(5));
        Assert.Equal("reply", r.Type);
        Assert.True(r.Data!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal(42, r.Data.Value.GetProperty("result").GetProperty("freedMb").GetInt32());

        var bad = await cli.SendCommandAsync("nope");
        Assert.False(bad.Data!.Value.GetProperty("ok").GetBoolean());
        var boom = await cli.SendCommandAsync("boom");
        Assert.Equal("kaboom", boom.Data!.Value.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Subscription_prefix_filters_events()
    {
        var id = NewId();
        var (srv, cli) = await Pair(id, "ram.");
        await using var _s = srv; await using var _c = cli;
        var got = new List<string>();
        cli.EventReceived += e => { lock (got) got.Add(e.Name!); };
        await Task.Delay(200); // subscribe islensin
        srv.Publish("download.completed", LinkJson.Obj(("id", "1")));
        srv.Publish("ram.cleaned", LinkJson.Obj(("freedMb", 10)));
        for (int i = 0; i < 50 && got.Count == 0; i++) await Task.Delay(50);
        await Task.Delay(200);
        lock (got) Assert.Equal(["ram.cleaned"], got);
    }

    [Fact]
    public async Task Progress_events_coalesce_per_key()
    {
        var id = NewId();
        var (srv, cli) = await Pair(id, "download.");
        await using var _s = srv; await using var _c = cli;
        await Task.Delay(200);
        var got = new List<int>();
        // Hizli yayin: istemci okumadan once kuyruga yigilir -> son deger kazanir.
        for (int i = 1; i <= 2000; i++) srv.Publish("download.progress", LinkJson.Obj(("pct", i)), key: "d1");
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50);
        }
        cli.EventReceived += e => { lock (got) got.Add(e.Data!.Value.GetProperty("pct").GetInt32()); };
        srv.Publish("download.progress", LinkJson.Obj(("pct", 2001)), key: "d1");
        await Task.Delay(300);
        lock (got) Assert.Contains(2001, got);
    }

    [Fact]
    public async Task Client_reconnects_after_server_restart()
    {
        var id = NewId();
        var (srv, cli) = await Pair(id);
        await using var _c = cli;
        await srv.DisposeAsync();
        await Task.Delay(300);
        var srv2 = new LinkServer(id, "1.0.1", [], ["ram.clean"]);
        srv2.OnCommand("ram.clean", (d, ct) => Task.FromResult<JsonElement?>(null));
        srv2.Start();
        await using var _s2 = srv2;
        for (int i = 0; i < 60 && !(cli.IsConnected && cli.Hello?.Version == "1.0.1"); i++) await Task.Delay(100);
        Assert.Equal("1.0.1", cli.Hello?.Version);
    }
}

public class RegistryTests
{
    [Fact]
    public void Write_read_remove_and_stale_cleanup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tsuite-reg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reg = new AppRegistry(dir);
            var exe = Path.Combine(dir, "alive.exe"); Directory.CreateDirectory(dir); File.WriteAllText(exe, "x");
            reg.Write(new AppManifest { Id = "alive", Name = "Alive", Version = "1.0.0", ExePath = exe, Commands = ["ram.clean"] });
            reg.Write(new AppManifest { Id = "gone", Name = "Gone", Version = "1.0.0", ExePath = Path.Combine(dir, "missing.exe") });
            reg.Write(new AppManifest { Id = "deadpid", Name = "D", Version = "1", ExePath = exe, Pid = 999999 });

            Assert.Equal("TSuite.alive", reg.Read("alive")!.PipeName);
            Assert.Equal(3, reg.ReadAll().Count);

            var removed = reg.CleanupStale();
            Assert.Equal(["gone"], removed);
            Assert.Null(reg.Read("gone"));
            Assert.Equal(0, reg.Read("deadpid")!.Pid);

            reg.Remove("alive");
            Assert.Null(reg.Read("alive"));
            Assert.Throws<ArgumentException>(() => reg.Write(new AppManifest { Id = "../evil" }));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
