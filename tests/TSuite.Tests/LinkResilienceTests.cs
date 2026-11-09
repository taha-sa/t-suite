using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TSuite.Link;

namespace TSuite.Tests;

public class LinkResilienceTests
{
    static string NewId() => LinkTests.NewId();

    static async Task<NamedPipeClientStream> Raw(string id)
    {
        var raw = new NamedPipeClientStream(".", LinkProtocol.PipeName(id), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await raw.ConnectAsync(5000);
        return raw;
    }

    static async Task Until(Func<bool> c, int ms = 8000) { for (int i = 0; i < ms / 40 && !c(); i++) await Task.Delay(40); }

    [Fact]
    public async Task Slow_reader_times_out_and_frees_slot()
    {
        var id = NewId();
        var srv = new LinkServer(id, "1.0.0", [], []) { WriteTimeout = TimeSpan.FromMilliseconds(400) };
        srv.Start();
        await using var _s = srv;
        using var raw = await Raw(id); // hic okumaz
        var sub = Encoding.UTF8.GetBytes("{\"v\":1,\"type\":\"subscribe\",\"data\":{\"prefixes\":[\"\"]}}\n");
        await raw.WriteAsync(sub); await raw.FlushAsync();
        await Until(() => srv.HasSubscriber("e."));
        Assert.Equal(1, srv.ClientCount);
        var big = LinkJson.Obj(("blob", new string('x', 200_000)));
        for (int i = 0; i < 60 && srv.ClientCount > 0; i++) { srv.Publish("e.big" + i, big); await Task.Delay(50); }
        await Until(() => srv.ClientCount == 0);
        Assert.Equal(0, srv.ClientCount);
    }

    [Fact]
    public async Task Command_flood_is_limited_per_client()
    {
        var id = NewId();
        var srv = new LinkServer(id, "1.0.0", [], ["slow"]) { MaxConcurrentCommandsPerClient = 4 };
        int cur = 0, max = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        srv.OnCommand("slow", async (d, ct) =>
        {
            var n = Interlocked.Increment(ref cur);
            lock (gate) max = Math.Max(max, n);
            await gate.Task;
            Interlocked.Decrement(ref cur);
            return null;
        });
        srv.Start();
        await using var _s = srv;
        await using var cli = new LinkClient(id, TimeSpan.FromMilliseconds(100));
        cli.Start();
        Assert.True(await cli.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
        var tasks = Enumerable.Range(0, 12).Select(_ => cli.SendCommandAsync("slow", timeout: TimeSpan.FromSeconds(10))).ToArray();
        await Until(() => tasks.Count(t => t.IsCompleted) >= 8);
        Assert.True(max <= 4);
        gate.SetResult();
        var rs = await Task.WhenAll(tasks);
        int busy = rs.Count(r => !r.Data!.Value.GetProperty("ok").GetBoolean() && r.Data.Value.GetProperty("error").GetString() == "busy");
        Assert.Equal(8, busy);
        Assert.True(max <= 4);
    }

    [Fact]
    public async Task Unread_replies_are_bounded_and_session_dropped()
    {
        var id = NewId();
        var srv = new LinkServer(id, "1.0.0", [], ["big"]) { MaxQueuedReplies = 3, WriteTimeout = TimeSpan.FromSeconds(30), MaxConcurrentCommandsPerClient = 1000 };
        var blob = LinkJson.Obj(("b", new string('y', 100_000)));
        srv.OnCommand("big", (d, ct) => Task.FromResult<JsonElement?>(blob));
        srv.Start();
        await using var _s = srv;
        using var raw = await Raw(id);
        for (int i = 0; i < 60; i++)
        {
            try { var b = Encoding.UTF8.GetBytes("{\"v\":1,\"type\":\"cmd\",\"id\":\"" + i + "\",\"name\":\"big\"}\n"); await raw.WriteAsync(b); await raw.FlushAsync(); }
            catch { break; }
            await Task.Delay(10);
        }
        await Until(() => srv.ClientCount == 0);
        Assert.Equal(0, srv.ClientCount);
    }

    [Fact]
    public async Task Terminal_event_drops_pending_progress_for_same_id()
    {
        var id = NewId();
        var srv = new LinkServer(id, "1.0.0", [], []);
        srv.Start();
        await using var _s = srv;
        await using var cli = new LinkClient(id, TimeSpan.FromMilliseconds(100));
        var got = new List<(string name, string? key)>();
        cli.EventReceived += e => { lock (got) got.Add((e.Name!, e.Id)); };
        cli.Start("");
        Assert.True(await cli.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
        await Until(() => srv.HasSubscriber("download.x"));
        for (int i = 0; i < 300; i++)
        {
            srv.Publish("download.progress", LinkJson.Obj(("pct", 99)), key: "k" + i);
            srv.Publish("download.completed", LinkJson.Obj(("id", "k" + i)), key: "k" + i);
        }
        await Until(() => { lock (got) return got.Count(g => g.name == "download.completed") == 300; });
        lock (got)
        {
            Assert.Equal(300, got.Count(g => g.name == "download.completed"));
            var done = new HashSet<string?>();
            foreach (var g in got)
            {
                if (g.name == "download.completed") done.Add(g.key);
                else if (g.name == "download.progress") Assert.DoesNotContain(g.key, done);
            }
        }
    }

    [Theory]
    [InlineData("{\"v\":1,\"type\":\"hello\",\"data\":{\"appId\":\"{ID}\",\"protocol\":99}}")]
    [InlineData("{\"v\":1,\"type\":\"hello\",\"data\":{\"appId\":\"other\",\"protocol\":1}}")]
    public async Task Client_rejects_hello_with_wrong_protocol_or_app(string hello)
    {
        var id = NewId();
        using var pipe = new NamedPipeServerStream(LinkProtocol.PipeName(id), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var cli = new LinkClient(id, TimeSpan.FromMilliseconds(100));
        bool connected = false;
        cli.Connected += _ => connected = true;
        cli.Start();
        await pipe.WaitForConnectionAsync(new CancellationTokenSource(5000).Token);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(hello.Replace("{ID}", id) + "\n")); await pipe.FlushAsync();
        await Task.Delay(500);
        Assert.False(connected);
        Assert.False(cli.IsConnected);
    }

    [Fact]
    public void Manifest_forward_fields_default_roundtrip_and_old_manifest_compat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "reg-" + Guid.NewGuid().ToString("N"));
        var reg = new AppRegistry(dir);
        reg.Write(new AppManifest { Id = "old", Name = "Old", Version = "1", ExePath = "x", Events = ["download.progress", "download.completed", "ram.status"] });
        var old = reg.Read("old")!;
        Assert.Null(old.ForwardPrefixes); Assert.Null(old.LaunchArgs);
        Assert.Equal(["download.", "ram."], old.EffectiveForwardPrefixes());
        Assert.Empty(old.EffectiveLaunchArgs());
        reg.Write(new AppManifest { Id = "new", Name = "N", Version = "1", ExePath = "x", ForwardPrefixes = ["a."], LaunchArgs = ["--tray"] });
        var n = reg.Read("new")!;
        Assert.Equal(["a."], n.EffectiveForwardPrefixes());
        Assert.Equal(["--tray"], n.EffectiveLaunchArgs());
        File.WriteAllText(Path.Combine(dir, "legacy.json"), "{\"id\":\"legacy\",\"name\":\"L\",\"events\":[\"scan.done\"]}");
        Assert.Equal(["scan."], reg.Read("legacy")!.EffectiveForwardPrefixes());
        Directory.Delete(dir, true);
    }
}
