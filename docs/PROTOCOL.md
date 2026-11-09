# T-Link / T-Update Protokol Spesifikasyonu (v1)

TR / EN. Tum T-Suite uygulamalari (T-Bridge, T-Downloader, T-RamCash, T-DeskTree ...) bu sozlesmeyi kullanir.
All T-Suite apps share this contract.

## 1. Kayit defteri / Registry

Her uygulama acilista `%LOCALAPPDATA%\T-Suite\apps\<id>.json` yazar (atomik: tmp + move) ve kapanista **silmez**
(hub "kurulu uygulamalar" listesini bundan alir; `pid` kapanista 0 yapilir). Kaldirilmis uygulamalar `exePath` yoksa temizlenir.

```json
{
  "id": "t-downloader",            // [A-Za-z0-9_-]+, degismez / immutable
  "name": "T-Downloader",
  "version": "1.4.0",
  "exePath": "C:\\Program Files\\T-Downloader\\TDownloader.App.exe",
  "pipeName": "TSuite.t-downloader",
  "events": ["download.progress", "download.completed", "download.failed"],
  "commands": ["download.add", "download.pause", "download.resume", "app.show", "app.version"],
  "releasesRepo": "taha-sa/t-downloader-releases",
  "icon": "C:\\...\\icon.ico",
  "pid": 12345,                     // 0 = calismiyor
  "forwardPrefixes": ["download.", "app.update."],  // ISTEGE BAGLI: hub'in telefona iletecegi olay onekleri
  "launchArgs": ["--tray"]          // ISTEGE BAGLI: kapaliyken baslatma argumanlari
}
```
* `forwardPrefixes` yoksa varsayilan `events` adlarindaki onekler (`download.progress` -> `download.`); `launchArgs` yoksa argumansiz. Mevcut uygulamalar (RamCash, Downloader, DeskTree) bu alanlar olmadan aynen calisir. SDK: `AppManifest.EffectiveForwardPrefixes()` / `EffectiveLaunchArgs()` (T-Bridge bunlari kullanir).
Bilinmeyen alanlar yoksayilir (ileri uyumluluk). Hub, uygulamayi `events`/`commands` listesinden **kesfeder**; kodda uygulama-ozel bilgi yoktur.

## 2. Named pipe

* Ad: `\\.\pipe\TSuite.<id>`; uygulama basina bir sunucu, cok istemci (varsayilan en fazla 16).
* **Guvenlik / Security:** `PipeOptions.CurrentUserOnly` -> pipe ACL'i yalnizca mevcut kullanici SID'ine izin verir; istemci de ayni bayragi kullanir.
* Kodlama: UTF-8, **her satir bir JSON zarfi**, `\n` ile biter (JSON-lines). Satir en fazla 1 MiB.
* Bozuk satirlar ve bilinmeyen `type` degerleri **yoksayilir** (baglanti kopmaz).

## 3. Zarf / Envelope

```json
{"v":1,"type":"event","id":null,"name":"download.progress","data":{...},"ts":1731140000000}
```
| alan | anlam |
|---|---|
| `v` | protokol surumu (int) |
| `type` | `hello` `subscribe` `cmd` `reply` `event` |
| `id` | `cmd`/`reply` iliskilendirme kimligi; `event`'te **coalesce anahtari** (ornegin indirme id'si) |
| `name` | olay veya komut adi (`alan.eylem`) |
| `data` | serbest JSON nesnesi |
| `ts` | Unix epoch ms |

Akis / flow:
1. Istemci baglanir -> sunucu `hello` yollar: `data = {appId, version, protocol, events[], commands[]}`.
2. Istemci `subscribe` yollar: `data = {"prefixes":["download.","disk."]}` (`""` = hepsi). **Varsayilan: abonelik yok**; her `subscribe` onceki listeyi degistirir. Eslesme: `name.StartsWith(prefix)`.
3. Istemci `cmd` yollar (`id` zorunlu); sunucu **tam bir** `reply` doner: `data = {"ok":true,"result":{...}}` veya `{"ok":false,"error":"..."}`. Bilinmeyen komut: `error = "unknown_command"`. Komut zaman asimi istemci tarafindadir (varsayilan 5 sn).
4. Sunucu abonelige uyan `event`'leri yayinlar.

## 4. Backpressure
Sunucu `Publish` asla bloklamaz (UI thread'inden cagrilabilir). Istemci basina: yanit kuyrugu (en fazla `MaxQueuedReplies`=256; dolunca yeni yanit yerine `{ok:false,error:"reply_queue_full"}` gider, 2x asilirsa oturum kapanir; en eski yanit dusurulmez), eszamanli komut siniri (`MaxConcurrentCommandsPerClient`=8; fazlasi `{ok:false,error:"busy"}`), yazim zaman asimi (`WriteTimeout`=10 sn; asilinca oturum kapanir), olay kuyrugu (en fazla 256; dolunca **en eski dusurulur**) ve coalesce tablosu. `*.progress` ve `ram.status` olaylari `(name, id)` basina **son deger kazanir** (ara degerler atilir). Bu nedenle `*.progress` olaylari **tam durum** tasimali (delta degil). **Sira garantisi:** `*.completed|failed|canceled` olayi bir kimlik (`id`) icin yayimlandiginda, o kimligin bekleyen coalesce girdileri (son `*.progress`) dusurulur; terminal olaydan sonra o kimlik icin progress gelmez.
**Istemci Hello dogrulamasi:** `LinkClient`, Hello'daki `protocol` ve `appId` beklenenle eslesmezse baglantiyi reddeder (`Connected` tetiklenmez, yeniden dener).

## 5. Ilk olaylar / Initial events
| ad | data |
|---|---|
| `download.progress` (id=indirme id) | `{id, name, bytes, total, speed, pct, state}` |
| `download.completed` | `{id, name, path, size, url}` |
| `download.failed` | `{id, name, error}` |
| `ram.status` | `{usedMb, totalMb, usedPct, standbyMb}` |
| `ram.cleaned` | `{freedMb, mode, durationMs}` |
| `disk.low` | `{drive, freeBytes, totalBytes, freePct}` |
| `app.update.available` | `{version, notes}` |
| `scan.finished` | `{root, totalBytes, durationMs}` |
| `file.open` (t-bridge -> telefon, suite olayi) | `{name}` |

## 6. Ilk komutlar / Initial commands
| ad | data -> result |
|---|---|
| `download.add` | `{url, referer?, dir?}` -> `{id}` |
| `download.pause` / `download.resume` | `{id}` -> `{}` |
| `ram.clean` | `{mode?: "smart"\|"full"}` -> `{freedMb}` |
| `app.show` | `{}` -> `{}` (pencereyi one getirir) |
| `app.version` | `{}` -> `{version, protocol}` |
| `disk.status` | `{drive?}` -> `{drives: [{drive, freeBytes, totalBytes, freePct}]}` |
| `scan.start` | `{path}` -> `{started}` |
| `file.present` | `{name}` -> `{}` |
| `file.send` | `{path, open?}` -> `{ids}` |
| `phone.status` | `{}` -> `{connected, name}` |

Komutlar uygulamanin kendi thread'ine yonlendirilmelidir: sunucu handler'i thread-pool'da calistirir, UI'ya dokunmadan once `Invoke` kullanin.

## 7. Surumleme kurallari / Versioning
* `v` yalnizca **uyumsuz** zarf degisikliginde artar. Ayni `v` icinde: yeni olay/komut/alan eklemek serbest; mevcutlari silmek veya anlamini degistirmek yasak.
* Alicilar bilinmeyen alan/olay/komut/type'i yoksayar. Sunucu `v` > desteklenen ise satiri yoksayar.
* Yeni uygulama yalnizca manifest yazar ve pipe acar; hub'da kod degisikligi gerekmez (kesif `events`/`commands` ile).
* Olay adlari `alan.eylem` bicimindedir; on ek aboneligi alan bazli calisir.

## 8. T-Update

`https://github.com/taha-sa/<app>-releases/releases/latest/download/latest.json`:
```json
{"version":"1.2.0","notes":"...","pub_date":"2026-11-09T12:00:00Z","url":"https://github.com/taha-sa/<app>-releases/releases/download/v1.2.0/<File>","sha256":"<hex>","signature":"<base64>"}
```
* **Bekleyen guncelleme (kalici):** "Sonra" denince `update-state.json` icinde `apps.<id>.pending` (`version,url,notes,sha256,pub_date,savedUtc`; IMZASIZ, yalniz gostermek icin) yazilir. 24 saat icinde acilista `UpdateController.LoadPending()` / `UpdateChecker.GetPending()` agsiz gosterir; kurulum `UpdateInfo.IsPending` ise once imzali manifesti yeniden alir. Durum dosyasi surecler arasi adlandirilmis Mutex (`Local\TSuite.Update.State`) ile korunur.
* **UpdateController API (geriye uyumlu eklemeler):** `CheckNow(force=true)` (MinInterval'i atlar), `PeriodicCheck(TimeSpan)` -> `IDisposable`, `LoadPending()`, `IsBusy`. `StartUpdateAsync` eszamanli cagrilarda tek indirme baslatir; `Later()`/`Cancel()` iptali `Failed` degil `Idle` yapar. `app.update.check` indirme/kurulum surerken `available:true` + mevcut surumu raporlar.
* Kontrol: acilista + en fazla 24 saatte bir (`%LOCALAPPDATA%\T-Suite\update-state.json`, uygulama basina `lastCheckUtc`, `skippedVersion`).
* Indirme URL'si **https** olmali. Gecici klasore indirilir.
* **Dogrulama (ikisi de zorunlu):** (1) dosyanin SHA-256 degeri `sha256` ile esit; (2) `signature` = ECDSA P-256 / SHA-256, IEEE-P1363 (r||s) base64, su UTF-8 mesaj uzerinde:
  `tsuite-update-v1\n<appId>\n<version>\n<sha256-hex-kucuk>`. Genel anahtar (base64 SPKI) her uygulamaya gomulur. Uyusmazlikta indirme silinir ve reddedilir. Surum + appId imzaya bagli: downgrade ve capraz-uygulama yeniden kullanimi engellenir.
* Uygulama dosyayi dogruladiktan sonra `TSuite.Updater.exe`'yi gecici klasore kopyalayip calistirir ve **hemen cikar**. Yardimci: ebeveyn PID'in cikisini bekler -> eski exe `.bak` -> yeni dosya yerine -> hata olursa geri al -> (istenirse) yeniden baslat. **Updater argumanlari:** `--pid N` (TEKRARLANABILIR; hepsinin cikisi beklenir, biri 60 sn icinde cikmazsa cikis kodu 3, hedef degismez), `--target <exe>`, `--source <dosya|.zip>`, `--sha256 <hex>` (kaynak, degistirmeden hemen once hashlenir; uyusmazlikta cikis kodu 3 ve hedef degismez; argumansiz eski davranis + log uyarisi; `LaunchUpdater(..., sha256:)` manifestteki `sha256`i gecirir), `--restart`. Log: `%LOCALAPPDATA%\T-Suite\logs\updater.log`. `.zip` kaynaklari hedef klasore acilir (yol-gecisi korumali).
* Anahtarlar: `C:\Users\hp\.t-suite\keys\<app>.pem` (ozel, repo disi) ve `<app>.pub`. Uretim/imza: `tools/keygen`.

### 8.1 Anahtar rotasyonu / Key rotation
* **latest.json ek alanlari (hepsi istege bagli; 1.1.0 istemcileri bilinmeyen alanlari yoksayar):** `key_id` = imzayi atan anahtarin kimligi (SPKI baytlarinin SHA-256 ilk 8 baytinin kucuk harf hex'i; `tsuite-keygen pub <app>` yazdirir). Varsa istemci YALNIZ o anahtari dener; yoksa tum guvenilir anahtarlari dener. `key_rotation` = `{"new_key":"<base64 SPKI>","new_key_id":"<hex>","revoke_key_ids":["<hex>"],"signature":"<base64>"}`.
* **Rotasyon imzasi:** ECDSA P-256/SHA-256, IEEE-P1363, ESKI (halen guvenilir) bir anahtarla, UTF-8 mesaj: `tsuite-keyrot-v1
<appId>
<new_key_id>
<new_key_base64>
<revoke idleri virgulle>`. `new_key_id` `new_key`ten hesaplananla esit olmali. Surum imza mesaji (`tsuite-update-v1...`) DEGISMEZ.
* **Istemci:** `UpdateOptions.TrustedPublicKeysBase64` (gomulu liste) + eski `PublicKeyBase64` (tek anahtara esdeger). Imza guvenilir anahtarlardan HERHANGI biriyle tutarsa kabul. Gecerli bir rotasyon kaydi gorulunce yeni anahtar `update-state.json` -> `apps.<id>.trustedKeys` icine, iptaller `apps.<id>.revokedKeyIds` icine kalici yazilir (etkin kume = gomulu + kalici - iptal). Yetkisiz/bozuk kayit sessizce yoksayilir. Son kalan anahtar iptal edilemez. Iptal edilmis anahtar tekrar eklenmez. Rotasyon kaydi her `CheckAsync` yanitinda islenir (guncel olsa bile).
* **Sira (kurulu 1.1.0 istemcilerle):** 1.1.0 istemcileri YALNIZ gomulu eski anahtari bilir ve rotasyon kaydini ogrenemez; bu yuzden (1) yeni anahtari uygulamalara gomup (`TrustedPublicKeysBase64`) yeni surumu ESKI anahtarla imzalayip yayinla (`tsuite-keygen rotate <app>` ile uretilen kaydi manifeste `key_rotation` olarak ekle) -> kurulu istemciler bu surumle yeni anahtari hem gomulu hem kalici olarak edinir; (2) istemcilerin cogu yeni surume gecince manifesti YENI anahtarla imzala (`key_id` ekle); 1.1.0'da kalanlar yeni imzayi dogrulayamaz, bu yuzden eski anahtarla imzali bir kopruden surum bir sure daha yayinlanmali; (3) bir surum sonra `revoke_key_ids` ile eski anahtari dusur. 1.1.0 istemcileri icin dogrudan (tek adimda) rotasyon MUMKUN DEGILDIR: gecis yolu yukaridaki kopru surumudur.
* **Iptal / sizinti:** eski ozel anahtar sizarsa saldirgan o anahtarla sahte surum ve (iptal edilmemisse) sahte rotasyon imzalayabilir. Hemen yeni anahtarla bir `key_rotation` (iptal listesinde sizan anahtar) yayinlayin; yeni anahtar bu kayit bir kez islenince gecerli olur, ancak henuz kaydi gormemis istemciler (ve 1.1.0) sizan anahtarla kandirilabilir: bu istemciler icin tek cozum yeni anahtar gomulu bir surumun elle kurulmasidir. `tsuite-keygen rotate <app> [iptalId,...]` kaydi eski ozel anahtarla imzalar, yeni cifti `<app>.next.pem/.pub` olarak yazar (ozel anahtar ASLA yazdirilmaz); sonra `<app>.next.*` dosyalari `<app>.*` olarak elle terfi ettirilir.

## 9. Standart guncelleme komutlari / Standard update commands



Istege bagli: `app.update.check` -> `{available, version}`; `app.update.install` -> `{started}` (`UpdateLink.Register`). Uygulamalar ayrica `--update` CLI bayragini destekler (kontrol, varsa indir + yardimci, cik; arayuz yok). `--update` calisirken AppRegistry'de canli (pid>0) bir tepsi ornegi varsa (`UpdateCli.FindRunningInstancePids`) onun pid'i de `--pid` olarak verilir (CloseMainWindow denenir), guncelleme sonrasi `--restart` uygulanir; ornek yoksa davranis aynidir. Not: ornek 60 sn icinde cikmazsa guncelleme iptal olur (hedef degismez). T-Bridge "Hepsini guncelle": calisanlara `app.update.install`, kapalilara `exe --update`. T-Bridge yalnizca `download.*`, `ram.*`, `disk.*`, `scan.*`, `app.update.*` onekli olaylari telefona iletir; ilerleme olaylari (`*.progress`) ogeye gore <= 2 Hz.

## 10. Uyumluluk / Compatibility

* **(a) Hello protocol:** `hello.protocol` veya `appId` beklenenle uyusmazsa istemci zarfi reddeder; `Connected` tetiklenmez ve baglanti yeniden denenir (bkz. bolum 3).
* **(b) Viewer-Bridge Messenger:** `WHAT_PING` uyum yoklamasi icindir. `WHAT_PRESENT_STATE` (101) eski Bridge surumlerinde yoktur; Viewer bu durumda 20 sn bekleyip geri duser.
* **(c) Viewer (APK/GitHub) guncelleyicisi:** T-Update imza semasindan bagimsizdir. Guven, Android paket imzasina dayanir.
