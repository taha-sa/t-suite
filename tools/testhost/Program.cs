using System.Text.Json;
using TSuite.Link;

// Birlikte çalışma testi sunucusu: tsuite-testhost <id>. Komut "echo" veriyi geri döner; abonelere 500 ms'de bir "tick.n" olayı yollar.
var id = args.Length > 0 ? args[0] : "tsuite-testhost";
await using var srv = new LinkServer(id, "1.0.0", ["tick.n"], ["echo"]);
srv.OnCommand("echo", (d, ct) => Task.FromResult<JsonElement?>(LinkJson.Obj(("echo", d ?? default(JsonElement)))));
srv.Start();
Console.WriteLine("READY");
for (int n = 1; ; n++)
{
    await Task.Delay(500);
    srv.Publish("tick.n", LinkJson.Obj(("n", n)));
}
