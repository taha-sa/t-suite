using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using TSuite.Update;

// tsuite-keygen gen <app>                        -> C:\Users\hp\.t-suite\keys\<app>.pem + <app>.pub
// tsuite-keygen pub <app>                        -> genel anahtari yazdirir
// sign <app> <version> <sha> [key.pem] [--min-version V] [--blocked-versions V,V] [--channel stable|beta] -> JSON: signature + signature2
// tsuite-keygen rotate <app> [revokeId,...]      -> <app>.next.pem/.pub uretir, rotasyon kaydini ESKI ozel anahtarla imzalar, JSON yazdirir
// Ozel anahtar ASLA yazdirilmaz / the private key is never printed.

var keyDir = Environment.GetEnvironmentVariable("TSUITE_KEYS")
             ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".t-suite", "keys");

if (args.Length < 2) { Console.Error.WriteLine("usage: gen|pub|rotate <app> | sign <app> <version> <sha256hex>"); return 2; }
var cmd = args[0]; var app = args[1];
var pem = Path.Combine(keyDir, app + ".pem"); var pub = Path.Combine(keyDir, app + ".pub");

switch (cmd)
{
    case "verify":
    {
        // verify <app> <public-key.pub> <manifest.json> <artifact.zip>
        // Reads only the public key and produced artifacts, never a private key.
        if (args.Length != 5) return 2;
        var publicKey = File.ReadAllText(args[2]).Trim();
        var manifest = System.Text.Json.JsonSerializer.Deserialize<LatestManifest>(File.ReadAllText(args[3]))
            ?? throw new InvalidDataException("Invalid manifest");
        bool valid = UpdateSigning.Sha256Hex(args[4]).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)
            && UpdateSigning.VerifySignature(publicKey, app, manifest.Version, manifest.Sha256, manifest.Signature)
            && UpdateSigning.VerifySignatureV2(publicKey, app, manifest);
        Console.WriteLine(valid ? $"Verified {app} {manifest.Version}: SHA-256, V1 and V2 signatures" : $"Verification failed: {app}");
        return valid ? 0 : 1;
    }
    case "gen":
        if (File.Exists(pem)) { Console.Error.WriteLine("exists, not overwriting: " + pem); Console.WriteLine(File.ReadAllText(pub).Trim()); return 1; }
        Directory.CreateDirectory(keyDir);
        var (priv, pubB64) = UpdateSigning.GenerateKeyPair();
        File.WriteAllText(pem, priv);
        // Ozel anahtar: kalitimi kapat, yalniz mevcut kullaniciya erisim ver.
        var pemInfo = new FileInfo(pem);
        var pemAcl = pemInfo.GetAccessControl();
        pemAcl.SetAccessRuleProtection(true, false);
        pemAcl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        pemInfo.SetAccessControl(pemAcl);
        File.WriteAllText(pub, pubB64);
        Console.WriteLine(pubB64);
        return 0;
    case "pub":
        Console.WriteLine(File.ReadAllText(pub).Trim());
        Console.WriteLine("key_id: " + UpdateSigning.KeyId(File.ReadAllText(pub).Trim()));
        return 0;
    case "rotate":
    {
        var nextPem = Path.Combine(keyDir, app + ".next.pem"); var nextPub = Path.Combine(keyDir, app + ".next.pub");
        if (File.Exists(nextPem)) { Console.Error.WriteLine("exists, not overwriting: " + nextPem); return 1; }
        var (nPriv, nPub) = UpdateSigning.GenerateKeyPair();
        File.WriteAllText(nextPem, nPriv); File.WriteAllText(nextPub, nPub);
        var revoke = args.Length > 2 ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        using var old = ECDsa.Create();
        old.ImportFromPem(File.ReadAllText(pem));
        var rec = UpdateSigning.SignRotation(old, app, nPub, revoke);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(rec));
        return 0;
    }
    case "sign":
        if (args.Length < 4) return 2;
        using (var ec = ECDsa.Create())
        {
            var index = 4;
            var keyPath = args.Length > index && !args[index].StartsWith("--") ? args[index++] : pem;
            var manifest = new LatestManifest { Version = args[2], Sha256 = args[3] };
            while (index < args.Length)
            {
                var option = args[index++];
                if (index >= args.Length) return 2;
                var value = args[index++];
                switch (option)
                {
                    case "--min-version": manifest.MinVersion = value; break;
                    case "--blocked-versions": manifest.BlockedVersions = value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                    case "--channel": manifest.Channel = value; break;
                    default: return 2;
                }
            }
            ec.ImportFromPem(File.ReadAllText(keyPath));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                signature = UpdateSigning.Sign(ec, app, manifest.Version, manifest.Sha256),
                signature2 = UpdateSigning.SignV2(ec, app, manifest)
            }));
        }
        return 0;
    default: return 2;
}
