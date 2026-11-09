using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using TSuite.Update;

// tsuite-keygen gen <app>                        -> C:\Users\hp\.t-suite\keys\<app>.pem + <app>.pub
// tsuite-keygen pub <app>                        -> genel anahtari yazdirir
// tsuite-keygen sign <app> <version> <sha256hex> [key.pem] -> imzayi (base64) yazdirir
// tsuite-keygen rotate <app> [revokeId,...]      -> <app>.next.pem/.pub uretir, rotasyon kaydini ESKI ozel anahtarla imzalar, JSON yazdirir
// Ozel anahtar ASLA yazdirilmaz / the private key is never printed.

var keyDir = Environment.GetEnvironmentVariable("TSUITE_KEYS")
             ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".t-suite", "keys");

if (args.Length < 2) { Console.Error.WriteLine("usage: gen|pub|rotate <app> | sign <app> <version> <sha256hex>"); return 2; }
var cmd = args[0]; var app = args[1];
var pem = Path.Combine(keyDir, app + ".pem"); var pub = Path.Combine(keyDir, app + ".pub");

switch (cmd)
{
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
            ec.ImportFromPem(File.ReadAllText(args.Length > 4 ? args[4] : pem));
            Console.WriteLine(UpdateSigning.Sign(ec, app, args[2], args[3]));
        }
        return 0;
    default: return 2;
}
