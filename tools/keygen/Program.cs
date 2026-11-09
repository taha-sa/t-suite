using System.Security.Cryptography;
using TSuite.Update;

// tsuite-keygen gen <app>                        -> C:\Users\hp\.t-suite\keys\<app>.pem + <app>.pub
// tsuite-keygen pub <app>                        -> genel anahtari yazdirir
// tsuite-keygen sign <app> <version> <sha256hex> -> imzayi (base64) yazdirir
// Ozel anahtar ASLA yazdirilmaz / the private key is never printed.

var keyDir = Environment.GetEnvironmentVariable("TSUITE_KEYS")
             ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".t-suite", "keys");

if (args.Length < 2) { Console.Error.WriteLine("usage: gen|pub <app> | sign <app> <version> <sha256hex>"); return 2; }
var cmd = args[0]; var app = args[1];
var pem = Path.Combine(keyDir, app + ".pem"); var pub = Path.Combine(keyDir, app + ".pub");

switch (cmd)
{
    case "gen":
        if (File.Exists(pem)) { Console.Error.WriteLine("exists, not overwriting: " + pem); Console.WriteLine(File.ReadAllText(pub).Trim()); return 1; }
        Directory.CreateDirectory(keyDir);
        var (priv, pubB64) = UpdateSigning.GenerateKeyPair();
        File.WriteAllText(pem, priv);
        File.WriteAllText(pub, pubB64);
        Console.WriteLine(pubB64);
        return 0;
    case "pub":
        Console.WriteLine(File.ReadAllText(pub).Trim());
        return 0;
    case "sign":
        if (args.Length < 4) return 2;
        using (var ec = ECDsa.Create())
        {
            ec.ImportFromPem(File.ReadAllText(pem));
            Console.WriteLine(UpdateSigning.Sign(ec, app, args[2], args[3]));
        }
        return 0;
    default: return 2;
}
