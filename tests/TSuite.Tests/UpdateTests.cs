using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TSuite.Update;

namespace TSuite.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.9.0", "1.10.0", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("1.2.3", "1.2.3-beta.1", false)]
    [InlineData("1.2.3-beta.1", "1.2.3", true)]
    [InlineData("1.2.3-beta.2", "1.2.3-beta.10", true)]
    [InlineData("2.0.0", "1.99.99", false)]
    [InlineData("1.0", "1.0.0.1", true)]
    public void Version_compare(string cur, string cand, bool newer) => Assert.Equal(newer, VersionUtil.IsNewer(cur, cand));

    static (string pem, string pub, string file, string sha, LatestManifest m) Make(string content = "payload")
    {
        var (pem, pub) = UpdateSigning.GenerateKeyPair();
        var f = Path.Combine(Path.GetTempPath(), "tsuite-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(f, content);
        var sha = UpdateSigning.Sha256Hex(f);
        using var ec = ECDsa.Create(); ec.ImportFromPem(pem);
        var m = new LatestManifest { Version = "2.0.0", Url = "https://x/y.bin", Sha256 = sha, Signature = UpdateSigning.Sign(ec, "app1", "2.0.0", sha) };
        return (pem, pub, f, sha, m);
    }

    [Fact]
    public void Signature_verifies_and_rejects_tampering()
    {
        var (_, pub, f, sha, m) = Make();
        UpdateSigning.VerifyFile(f, m, "app1", pub); // ok

        Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifyFile(f, m, "other-app", pub)); // capraz uygulama
        m.Version = "1.0.0"; // downgrade/surum degisikligi
        Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifyFile(f, m, "app1", pub));
        m.Version = "2.0.0";

        File.WriteAllText(f, "tampered"); // hash uyusmazligi
        var ex = Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifyFile(f, m, "app1", pub));
        Assert.Contains("SHA-256", ex.Message);

        var (_, otherPub) = UpdateSigning.GenerateKeyPair(); // yanlis anahtar
        File.WriteAllText(f, "payload");
        Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifyFile(f, m, "app1", otherPub));
        Assert.False(UpdateSigning.VerifySignature(pub, "app1", "2.0.0", sha, "!!notbase64"));
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(f(r)); }
    }

    [Fact]
    public async Task Checker_end_to_end_with_24h_throttle_skip_and_download()
    {
        var (pem, pub, _, _, _) = Make();
        var payload = Encoding.UTF8.GetBytes("new-exe-bytes");
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        using var ec = ECDsa.Create(); ec.ImportFromPem(pem);
        var latest = new LatestManifest { Version = "1.1.0", Notes = "n", Url = "https://dl/app.exe", Sha256 = sha, Signature = UpdateSigning.Sign(ec, "app1", "1.1.0", sha) };
        var json = JsonSerializer.Serialize(latest);
        var h = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("latest.json")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var state = Path.Combine(Path.GetTempPath(), "tsuite-st-" + Guid.NewGuid().ToString("N") + ".json");
        var chk = new UpdateChecker(new UpdateOptions
        {
            AppId = "app1", CurrentVersion = "1.0.0", PublicKeyBase64 = pub, StatePath = state,
            ManifestUrl = "https://x/latest.json", Http = new HttpClient(h)
        });

        var info = await chk.CheckAsync();
        Assert.NotNull(info); Assert.Equal("1.1.0", info!.NewVersion);
        Assert.Null(await chk.CheckAsync());          // 24 saat dolmadi: ag cagrisi yok
        Assert.Equal(1, h.Calls);
        Assert.NotNull(await chk.CheckAsync(force: true));

        var path = await chk.DownloadAsync(info);
        Assert.Equal(payload, File.ReadAllBytes(path));

        // bozuk indirme reddedilir ve dosya silinir
        var bad = new HttpClient(new Handler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }));
        var chk2 = new UpdateChecker(new UpdateOptions { AppId = "app1", CurrentVersion = "1.0.0", PublicKeyBase64 = pub, StatePath = state, Http = bad });
        await Assert.ThrowsAsync<UpdateVerificationException>(() => chk2.DownloadAsync(info));

        chk.SkipVersion("1.1.0");
        Assert.Null(await chk.CheckAsync(force: false)); // throttle de var; force ile skip yoksayilir
        Assert.NotNull(await chk.CheckAsync(force: true));
    }

    [Fact]
    public async Task Insecure_download_url_rejected()
    {
        var (_, pub, _, _, m) = Make();
        m.Url = "http://evil/app.exe";
        var chk = new UpdateChecker(new UpdateOptions { AppId = "app1", CurrentVersion = "1", PublicKeyBase64 = pub });
        await Assert.ThrowsAsync<UpdateVerificationException>(() =>
            chk.DownloadAsync(new UpdateInfo("1", "2.0.0", "", null, m)));
    }
}
