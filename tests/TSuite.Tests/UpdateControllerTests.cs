using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TSuite.Update;

namespace TSuite.Tests;

public class UpdateControllerTests
{
    sealed class GateHandler(string manifestJson, byte[] payload) : HttpMessageHandler
    {
        public int Downloads;
        public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("latest.json"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
            Interlocked.Increment(ref Downloads);
            await Gate.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }
    }

    static (UpdateController ctl, GateHandler h, Func<UpdateController> second) Setup()
    {
        var (pem, pub) = UpdateSigning.GenerateKeyPair();
        var payload = Encoding.UTF8.GetBytes("exe");
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        using var ec = ECDsa.Create(); ec.ImportFromPem(pem);
        var latest = new LatestManifest { Version = "9.0.0", Notes = "n", Url = "https://dl/app.exe", Sha256 = sha, Signature = UpdateSigning.Sign(ec, "ctl1", "9.0.0", sha) };
        var h = new GateHandler(JsonSerializer.Serialize(latest), payload);
        var state = Path.Combine(Path.GetTempPath(), "ctl-" + Guid.NewGuid().ToString("N") + ".json");
        UpdateChecker Mk() => new(new UpdateOptions { AppId = "ctl1", CurrentVersion = "1.0.0", PublicKeyBase64 = pub, StatePath = state, ManifestUrl = "https://x/latest.json", Http = new HttpClient(h) });
        return (new UpdateController(Mk()), h, () => new UpdateController(Mk()));
    }

    static async Task WaitFor(Func<bool> c) { for (int i = 0; i < 100 && !c(); i++) await Task.Delay(30); Assert.True(c()); }

    [Fact]
    public async Task Concurrent_install_starts_single_download_and_Later_cancels_to_Idle()
    {
        var (ctl, h, _) = Setup();
        Assert.True(await ctl.CheckAsync(force: true));
        var t1 = ctl.StartUpdateAsync();
        var t2 = ctl.StartUpdateAsync();
        var t3 = ctl.StartUpdateAsync();
        await WaitFor(() => h.Downloads >= 1);
        await Task.Delay(150);
        Assert.Equal(1, h.Downloads);
        Assert.Equal(UpdateState.Downloading, ctl.State);
        ctl.Later();
        await Task.WhenAll(t1, t2, t3);
        Assert.Equal(UpdateState.Idle, ctl.State);
        Assert.Null(ctl.Error);
        Assert.False(ctl.IsBusy);
    }

    [Fact]
    public async Task Later_persists_pending_and_second_process_sees_it_offline()
    {
        var (ctl, h, second) = Setup();
        Assert.True(await ctl.CheckAsync(force: true));
        ctl.Later();
        var c2 = second();
        Assert.True(c2.LoadPending());
        Assert.Equal(UpdateState.Available, c2.State);
        Assert.Equal("9.0.0", c2.Info!.NewVersion);
        Assert.True(c2.Info.IsPending);
        // MinInterval dolmadi: ag yok ama bekleyen donmeli
        var c3 = second();
        Assert.True(await c3.CheckAsync(force: false));
        Assert.Equal(0, h.Downloads);
        c3.Skip();
        Assert.False(second().LoadPending());
    }

    [Fact]
    public async Task UpdateLink_check_during_download_reports_available()
    {
        var (ctl, h, _) = Setup();
        var id = "ulc" + Guid.NewGuid().ToString("N")[..8];
        await using var srv = new TSuite.Link.LinkServer(id, "1.0.0", [], UpdateLink.Commands);
        UpdateLink.Register(srv, ctl);
        srv.Start();
        await using var cli = new TSuite.Link.LinkClient(id, TimeSpan.FromMilliseconds(100));
        cli.Start();
        Assert.True(await cli.WaitConnectedAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await ctl.CheckAsync(force: true));
        var t = ctl.StartUpdateAsync();
        await WaitFor(() => h.Downloads >= 1);
        var r = await cli.SendCommandAsync(UpdateLink.Check, timeout: TimeSpan.FromSeconds(5));
        var res = r.Data!.Value.GetProperty("result");
        Assert.True(res.GetProperty("available").GetBoolean());
        Assert.Equal("9.0.0", res.GetProperty("version").GetString());
        ctl.Cancel(); await t;
        Assert.Equal(UpdateState.Idle, ctl.State);
    }
}
