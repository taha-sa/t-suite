using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using TSuite.Update;

namespace TSuite.Tests;

public class UpdateSafetyTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    sealed class Fixture : IDisposable
    {
        public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Pub => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
        public readonly string State = Path.Combine(Path.GetTempPath(), "safety-" + Guid.NewGuid() + ".json");
        public readonly LatestManifest M = new() { Version = "2.0.0", Url = "https://x/app.exe", Sha256 = new string('a', 64) };
        public readonly List<string> Requests = [];
        public void Sign()
        {
            M.Signature = UpdateSigning.Sign(Key, "app", M.Version, M.Sha256);
            M.Signature2 = UpdateSigning.SignV2(Key, "app", M);
        }
        public UpdateChecker Checker(UpdateChannel channel = UpdateChannel.Stable, bool missingBeta = false, string app = "app") => new(new UpdateOptions
        {
            AppId = app, CurrentVersion = "1.1.0", PublicKeyBase64 = Pub, StatePath = State,
            ManifestUrl = "https://x/latest.json", Channel = channel, MinInterval = TimeSpan.Zero,
            Http = new HttpClient(new Handler(r =>
            {
                Requests.Add(r.RequestUri!.AbsolutePath);
                return new HttpResponseMessage(missingBeta && r.RequestUri.AbsolutePath == "/latest-beta.json" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                    { Content = new StringContent(JsonSerializer.Serialize(M)) };
            }))
        });
        public void Dispose() { Key.Dispose(); File.Delete(State); }
    }

    [Theory]
    [InlineData("1.2.0", false, true)]
    [InlineData("1.1.0", false, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public async Task Required_rules_override_skip_and_controller_deferral(string? min, bool blocked, bool required)
    {
        using var f = new Fixture();
        if (min != null) f.M.MinVersion = min;
        f.M.BlockedVersions = blocked ? ["1.1.0"] : [];
        f.Sign();
        var checker = f.Checker();
        checker.SkipVersion("2.0.0");
        var ctl = new UpdateController(checker);
        Assert.Equal(required, await ctl.CheckAsync());
        if (!required) return;
        Assert.True(ctl.Info!.IsRequired);
        ctl.Later(); ctl.Skip();
        Assert.Equal(UpdateState.Available, ctl.State);
        Assert.NotNull(ctl.Info);
    }

    [Fact]
    public async Task Blocked_offer_never_downloaded_even_if_forced_or_direct()
    {
        using var f = new Fixture();
        f.M.BlockedVersions = ["2.0.0"]; f.Sign();
        var c = f.Checker();
        Assert.Null(await c.CheckAsync(true));
        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(new UpdateInfo("1.1.0", "2.0.0", "", null, f.M)));
        Assert.Single(f.Requests);
    }

    [Fact]
    public void V2_file_verification_and_explicit_null_field_require_authentication()
    {
        using var f = new Fixture();
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "signed payload");
            f.M.Sha256 = UpdateSigning.Sha256Hex(file);
            f.M.Channel = "stable"; f.Sign();
            UpdateSigning.VerifyFile(file, f.M, "app", f.Pub);
            UpdateSigning.VerifyFile(file, f.M, "app", new[] { f.Pub });
            f.M.MinVersion = "1.2.0";
            Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifyFile(file, f.M, "app", f.Pub));
            Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifyFile(file, f.M, "app", new[] { f.Pub }));
            var nullField = JsonSerializer.Deserialize<LatestManifest>("{\"min_version\":null}")!;
            Assert.True(nullField.HasSafetyFields);
            Assert.Throws<UpdateVerificationException>(() => UpdateSigning.VerifySafetyFields([f.Pub], "app", nullField));
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("min")]
    [InlineData("blocked")]
    [InlineData("channel")]
    [InlineData("signature")]
    public async Task V2_fields_are_authenticated(string field)
    {
        using var f = new Fixture();
        f.M.MinVersion = "1.2.0"; f.M.BlockedVersions = []; f.M.Channel = "stable"; f.Sign();
        Assert.NotNull(await f.Checker().CheckAsync(true));
        Assert.True(UpdateSigning.VerifySignatureV2(f.Pub, "app", f.M));
        Assert.False(UpdateSigning.VerifySignatureV2(f.Pub, "other", f.M));
        switch (field)
        {
            case "min": f.M.MinVersion = "1.3.0"; break;
            case "blocked": f.M.BlockedVersions.Add("1.1.0"); break;
            case "channel": f.M.Channel = "beta"; break;
            case "signature": f.M.Signature2 = null; break;
        }
        await Assert.ThrowsAsync<UpdateVerificationException>(() => f.Checker().CheckAsync(true));
        await Assert.ThrowsAsync<UpdateVerificationException>(() => f.Checker().DownloadAsync(new UpdateInfo("1.1.0", "2.0.0", "", null, f.M)));
    }

    [Fact]
    public async Task Both_compatibility_directions_and_exact_canonical_message()
    {
        using var f = new Fixture();
        f.Sign(); f.M.Signature2 = null; // old manifest, v1 only
        Assert.NotNull(await f.Checker().CheckAsync(true));
        f.M.MinVersion = "1.2.0"; f.M.BlockedVersions = ["1.0.0", "1.0.1"]; f.M.Channel = "stable"; f.Sign();
        Assert.Equal("tsuite-update-v2\napp\n2.0.0\n" + f.M.Sha256 + "\n1.2.0\n1.0.0,1.0.1\nstable",
            System.Text.Encoding.UTF8.GetString(UpdateSigning.CanonicalV2("app", f.M)));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(f.M));
        // The installed v1 client's verification inputs are unchanged.
        Assert.True(UpdateSigning.VerifySignature(f.Pub, "app", json.RootElement.GetProperty("version").GetString()!,
            json.RootElement.GetProperty("sha256").GetString()!, json.RootElement.GetProperty("signature").GetString()!));
    }

    [Theory]
    [InlineData(UpdateChannel.Stable, false, "/latest.json")]
    [InlineData(UpdateChannel.Beta, false, "/latest-beta.json")]
    [InlineData(UpdateChannel.Beta, true, "/latest-beta.json,/latest.json")]
    public async Task Channel_selection_and_missing_file_fallback(UpdateChannel channel, bool missing, string paths)
    {
        using var f = new Fixture(); f.Sign();
        Assert.NotNull(await f.Checker(channel, missing).CheckAsync(true));
        Assert.Equal(paths, string.Join(",", f.Requests));
    }

    [Fact]
    public async Task Channel_persists_per_app_and_clears_cached_offer()
    {
        using var f = new Fixture(); f.Sign();
        var ctl = new UpdateController(f.Checker());
        Assert.True(await ctl.CheckAsync(true));
        ctl.Later();
        ctl.Channel = UpdateChannel.Beta;
        Assert.Null(ctl.Info);
        Assert.Equal(UpdateChannel.Beta, new UpdateController(f.Checker()).Channel);
        Assert.Null(f.Checker().GetPending());
        Assert.Equal(UpdateChannel.Stable, f.Checker(app: "other").Channel);
        ctl.Channel = UpdateChannel.Stable;
        Assert.Equal(UpdateChannel.Stable, f.Checker(UpdateChannel.Beta).Channel);
    }

    [Fact]
    public async Task Stable_rejects_beta_offer_and_bad_beta_signature_does_not_fall_back()
    {
        using var f = new Fixture(); f.M.Channel = "beta"; f.Sign();
        Assert.Null(await f.Checker().CheckAsync(true));
        f.Requests.Clear(); f.M.Signature2 = "invalid";
        await Assert.ThrowsAsync<UpdateVerificationException>(() => f.Checker(UpdateChannel.Beta).CheckAsync(true));
        Assert.Equal(["/latest-beta.json"], f.Requests);
    }
}
