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
  "pid": 12345                      // 0 = calismiyor
}
```
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
Sunucu `Publish` asla bloklamaz (UI thread'inden cagrilabilir). Istemci basina: yanit kuyrugu (hic dusmez), olay kuyrugu (en fazla 256; dolunca **en eski dusurulur**) ve coalesce tablosu. `*.progress` ve `ram.status` olaylari `(name, id)` basina **son deger kazanir** (ara degerler atilir). Bu nedenle `*.progress` olaylari **tam durum** tasimali (delta degil).

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

## 6. Ilk komutlar / Initial commands
| ad | data -> result |
|---|---|
| `download.add` | `{url, referer?, dir?}` -> `{id}` |
| `download.pause` / `download.resume` | `{id}` -> `{}` |
| `ram.clean` | `{mode?: "smart"\|"full"}` -> `{freedMb}` |
| `app.show` | `{}` -> `{}` (pencereyi one getirir) |
| `app.version` | `{}` -> `{version, protocol}` |

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
* Kontrol: acilista + en fazla 24 saatte bir (`%LOCALAPPDATA%\T-Suite\update-state.json`, uygulama basina `lastCheckUtc`, `skippedVersion`).
* Indirme URL'si **https** olmali. Gecici klasore indirilir.
* **Dogrulama (ikisi de zorunlu):** (1) dosyanin SHA-256 degeri `sha256` ile esit; (2) `signature` = ECDSA P-256 / SHA-256, IEEE-P1363 (r||s) base64, su UTF-8 mesaj uzerinde:
  `tsuite-update-v1\n<appId>\n<version>\n<sha256-hex-kucuk>`. Genel anahtar (base64 SPKI) her uygulamaya gomulur. Uyusmazlikta indirme silinir ve reddedilir. Surum + appId imzaya bagli: downgrade ve capraz-uygulama yeniden kullanimi engellenir.
* Uygulama dosyayi dogruladiktan sonra `TSuite.Updater.exe`'yi gecici klasore kopyalayip calistirir ve **hemen cikar**. Yardimci: ebeveyn PID'in cikisini bekler -> eski exe `.bak` -> yeni dosya yerine -> hata olursa geri al -> (istenirse) yeniden baslat. Log: `%LOCALAPPDATA%\T-Suite\logs\updater.log`. `.zip` kaynaklari hedef klasore acilir (yol-gecisi korumali).
* Anahtarlar: `C:\Users\hp\.t-suite\keys\<app>.pem` (ozel, repo disi) ve `<app>.pub`. Uretim/imza: `tools/keygen`.

## 9. Standart guncelleme komutlari / Standard update commands



Istege bagli: `app.update.check` -> `{available, version}`; `app.update.install` -> `{started}` (`UpdateLink.Register`). Uygulamalar ayrica `--update` CLI bayragini destekler (kontrol, varsa indir + yardimci, cik; arayuz yok). T-Bridge "Hepsini guncelle": calisanlara `app.update.install`, kapalilara `exe --update`. T-Bridge yalnizca `download.*`, `ram.*`, `disk.*`, `scan.*`, `app.update.*` onekli olaylari telefona iletir; ilerleme olaylari (`*.progress`) ogeye gore <= 2 Hz.
