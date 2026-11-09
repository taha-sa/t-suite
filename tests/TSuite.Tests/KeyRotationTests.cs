using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TSuite.Update;

namespace TSuite.Tests;

public class KeyRotationTests
{
    sealed class H(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    sealed class Key
    {
        public ECDsa Ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Pub => Convert.ToBase64String(Ec.ExportSubjectPublicKeyInfo());
        public string Id => UpdateSigning.KeyId(Pub);
    }

    static readonly byte[] Payload = Encoding.UTF8.GetBytes("new-exe");

    static LatestManifest Manifest(Key signer, string version = "1.1.0", bool withKeyId = false)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(Payload));
        return new LatestManifest
        {
            Version = version, Url = "https://dl/app.exe", Sha256 = sha,
            Signature = UpdateSigning.Sign(signer.Ec, "app1", version, sha), KeyId = withKeyId ? signer.Id : null
        };
    }

    static UpdateChecker Checker(Func<LatestManifest> current, string state, string? pub = null, IReadOnlyList<string>? list = null)
        => new(new UpdateOptions
        {
            AppId = "app1", CurrentVersion = "1.0.0", StatePath = state, ManifestUrl = "https://x/latest.json",
            PublicKeyBase64 = pub ?? "", TrustedPublicKeysBase64 = list,
            Http = new HttpClient(new H(r => r.RequestUri!.AbsolutePath.EndsWith("latest.json")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(current())) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }))
        });

    static string NewState() => Path.Combine(Path.GetTempPath(), "tsuite-kr-" + Guid.NewGuid().ToString("N") + ".json");

    static async Task<bool> Downloads(UpdateChecker c)
    {
        var info = await c.CheckAsync(force: true);
        if (info == null) return false;
        try { var f = await c.DownloadAsync(info); File.Delete(f); return true; }
        catch (UpdateVerificationException) { return false; }
    }

    [Fact]
    public async Task Legacy_single_key_config_unchanged()
    {
        var a = new Key();
        Assert.True(await Downloads(Checker(() => Manifest(a), NewState(), pub: a.Pub)));
        Assert.False(await Downloads(Checker(() => Manifest(new Key()), NewState(), pub: a.Pub)));
    }

    [Fact]
    public async Task Second_key_in_list_accepts_package_and_key_id_restricts()
    {
        Key a = new(), b = new();
        var list = new[] { b.Pub };
        Assert.True(await Downloads(Checker(() => Manifest(b), NewState(), a.Pub, list)));
        Assert.True(await Downloads(Checker(() => Manifest(b, withKeyId: true), NewState(), a.Pub, list)));
        // key_id a'yi gosteriyor ama imza b'den: yalniz a denenir -> red
        Assert.False(await Downloads(Checker(() => { var m = Manifest(b); m.KeyId = a.Id; return m; }, NewState(), a.Pub, list)));
    }

    [Fact]
    public async Task Unauthorized_rotation_rejected_and_key_not_added()
    {
        Key a = new(), attacker = new(), evil = new();
        var rot = UpdateSigning.SignRotation(attacker.Ec, "app1", evil.Pub);
        var st = NewState();
        var c = Checker(() => { var m = Manifest(evil); m.KeyRotation = rot; return m; }, st, a.Pub);
        Assert.False(await Downloads(c));
        Assert.Single(c.GetTrustedKeys());
        Assert.DoesNotContain("trustedKeys", File.Exists(st) ? File.ReadAllText(st) : "");
        // baska uygulama icin imzalanmis gecerli kayit da gecersiz
        var other = UpdateSigning.SignRotation(a.Ec, "other-app", evil.Pub);
        var c2 = Checker(() => { var m = Manifest(a); m.KeyRotation = other; return m; }, NewState(), a.Pub);
        await c2.CheckAsync(force: true);
        Assert.Single(c2.GetTrustedKeys());
    }

    [Fact]
    public async Task Valid_rotation_persists_new_key_and_revoke_drops_old()
    {
        Key a = new(), b = new();
        var st = NewState();
        var rot = UpdateSigning.SignRotation(a.Ec, "app1", b.Pub);
        // Rotasyon kaydi eski anahtarla imzali surumle gelir.
        var c = Checker(() => { var m = Manifest(a); m.KeyRotation = rot; return m; }, st, a.Pub);
        Assert.True(await Downloads(c));
        Assert.Equal(2, c.GetTrustedKeys().Count);
        Assert.Contains("trustedKeys", File.ReadAllText(st));

        // Yeni oturum: yalniz gomulu eski anahtar; yeni anahtarla imzali surum kalici listeden kabul edilir.
        var c2 = Checker(() => Manifest(b), st, a.Pub);
        Assert.True(await Downloads(c2));

        // Eski anahtari iptal et (yeni anahtarla imzali kayit gerekmez: eski de hala guvenilir ama yeni de imzalayabilir).
        var revoke = UpdateSigning.SignRotation(b.Ec, "app1", b.Pub, [a.Id]);
        var c3 = Checker(() => { var m = Manifest(b); m.KeyRotation = revoke; return m; }, st, a.Pub);
        Assert.True(await Downloads(c3));
        Assert.Equal([b.Pub], c3.GetTrustedKeys());
        Assert.False(await Downloads(Checker(() => Manifest(a), st, a.Pub)));
        Assert.True(await Downloads(Checker(() => Manifest(b), st, a.Pub)));
    }

    [Fact]
    public async Task Last_key_cannot_be_revoked()
    {
        var a = new Key();
        var st = NewState();
        var rot = UpdateSigning.SignRotation(a.Ec, "app1", a.Pub, [a.Id]);
        var c = Checker(() => { var m = Manifest(a); m.KeyRotation = rot; return m; }, st, a.Pub);
        Assert.True(await Downloads(c));
        Assert.Equal([a.Pub], c.GetTrustedKeys());
        Assert.True(await Downloads(Checker(() => Manifest(a), st, a.Pub)));
    }

    [Fact]
    public void Old_client_ignores_new_manifest_fields_and_rotation_message_format()
    {
        var a = new Key(); var b = new Key();
        var rot = UpdateSigning.SignRotation(a.Ec, "app1", b.Pub, ["deadbeefdeadbeef"]);
        Assert.Equal(16, rot.NewKeyId.Length);
        Assert.True(UpdateSigning.VerifyRotation([a.Pub], "app1", rot));
        rot.RevokeKeyIds = [];
        Assert.False(UpdateSigning.VerifyRotation([a.Pub], "app1", rot));
        var json = JsonSerializer.Serialize(new LatestManifest { Version = "1.2.0", KeyId = a.Id, KeyRotation = rot });
        Assert.Contains("\"key_rotation\"", json);
        Assert.Contains("\"new_key_id\"", json);
    }
}
