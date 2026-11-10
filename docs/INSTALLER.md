# T-Suite kurulum programları (Inno Setup 6)

Klasör: `installer/`. Çıktı: `installer/out/` (git'e girmez).

| Dosya | Görev |
|---|---|
| `t-ramcash.iss`, `t-downloader.iss`, `t-desktree.iss` | Uygulama başına kurulum (ince sarmalayıcı; ortak şablon `app.iss.inc`) |
| `t-suite-setup.iss` | "T-Suite Kurulum": bileşen seçmeli (T-Bridge, T-Downloader, T-RamCash, T-DeskTree, T-Desk Viewer = Telefon için) |
| `apps.iss.inc` | Ad / exe / Run değeri / mutex / GUID sabitleri (tek yerde) |
| `common.iss.inc` | Diller (tr/en/de), yerelleştirilmiş mesajlar, .NET denetimi, manifest taslağı, kaldırma temizliği |
| `assets/*.bmp` | Sihirbaz görselleri; `tools/make_wizard_images.py` üretir (ikon + vurgu rengi, koyu T-Suite zemini; ayrıca `viewer-qr.bmp`) |
| `build.ps1` | publish + derleme |

## Neden kullanıcı başına (per-user)?
`PrivilegesRequired=lowest`, hedef `%LOCALAPPDATA%\Programs\T-Suite\<Ad>\`. Yönetici izni gerekmez; T-Update uygulamanın kendi klasörüne yazabildiği için sessizce güncelleyebilir; Programlar ve Özellikler kaydı HKCU altındadır. Tek istisna .NET çalışma zamanı kurulumu (aşağıda) — yalnız o adımda UAC sorulur.

## Düzen ve exe adları
T-Bridge'in otomatik başlatma yedeği `%LOCALAPPDATA%\Programs\T-Suite\<Ad>\<Ad>.exe` bekler (`kopru/desktop/src-tauri/src/suite.rs`).

| Uygulama | Klasör | Exe | Uyum |
|---|---|---|---|
| T-RamCash | `T-RamCash` | `T-RamCash.exe` | evet |
| T-DeskTree | `T-DeskTree` | `T-DeskTree.exe` | evet |
| T-Downloader | `T-Downloader` | `TDownloader.exe` (+ `TDownloader.Host.exe`) | **hayır** (tire yok) |

T-Downloader için yedek yol (`T-Downloader\T-Downloader.exe`) tutmaz; bu yüzden kurulum, `%LOCALAPPDATA%\T-Suite\apps\<id>.json` AppRegistry dosyasına **taslak manifest** yazar (`id, name, version, exePath, pipeName`). Hub önce manifesti kullandığı için sorun olmaz. Taslak, var olan manifesti ezmez; uygulama ilk açılışında kendi tam manifestini yazar. Kalıcı çözüm için T-Bridge yedek yolu `exePath` ya da `TDownloader.exe` adını da denemeli (öneri). Üç uygulama için de taslak yazılır.

## Görevler / seçenekler
- Başlat menüsü: `T-Suite\<Ad>`; masaüstü kısayolu isteğe bağlı.
- "Windows ile başlat" (uygulamaların kendi yazdığı HKCU Run değeriyle birebir): RamCash `T-RamCash` = `"<exe>" --tray`; Downloader `T-Downloader` = `"<exe>" --background`; DeskTree `T-DeskTree` = `"<exe>" --background`. Varsayılan: RamCash/Downloader açık (`checkedonce`: sonraki kurulumlarda kullanıcı tercihi hatırlanır), DeskTree kapalı. Not: uygulamalar kendi ayarlarından Run değerini yeniden yazabilir (RamCash `StartWithWindows` ayarı) — kurulum ile uygulama ayarı çelişirse uygulama ayarı kazanır.
- Çalışan örnek: `CloseApplications=yes` (Restart Manager) + `AppMutex` (uygulamaların tek-örnek mutex adları).
- Kaldırma: HKCU Run değeri ve manifest silinir (yalnız bu kurulumun yolunu gösteriyorsa). Ayarlar (`%APPDATA%\T-Desk\T-RamCash`, `%APPDATA%\T-Downloader`, `%APPDATA%\AmberTree`, günlükler) varsayılan olarak **korunur**; kaldırıcı "Ayarlar da silinsin mi?" sorar (Evet/Hayır, varsayılan Hayır). Sessiz kaldırmada `/DELETESETTINGS=1` ile silinir.
- T-Downloader: `com.tdownloader.host` native messaging kaydı Chrome, Edge ve Brave için HKCU'ya yazılır; host manifest JSON'u `%APPDATA%\T-Downloader\native-host\` altına (`installer\install-host.ps1` ile aynı mantık, aynı eklenti kimliği; `/DExtensionId=` ile değiştirilebilir). Eklenti dosyaları `<kurulum>\extension` altına kopyalanır; kurulum sonunda "eklenti klasörünü aç" seçeneği ve yönerge (chrome://extensions → Geliştirici modu → Paketlenmemiş öğe yükle) sunulur.

## .NET 10 Desktop Runtime
Uygulamalar framework-dependent. Kurulum, `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` altında `10.*` değeri arar. Yoksa (`PrepareToInstall`) `https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe` indirilir (ilerleme çubuğuyla) ve `/install /quiet /norestart` ile `runas` altında kurulur (UAC). Çevrimdışı/başarısızsa kurulum, yerelleştirilmiş mesajla (indirme bağlantısıyla) durur. Suite kurulumunda yalnız .NET gerektiren bileşen seçiliyse denetlenir.

## T-Suite Kurulum
- Bileşenler: T-Bridge, T-Downloader, T-RamCash, T-DeskTree, T-Desk Viewer (yalnız bilgi — "Telefon için"; seçilirse QR/bağlantı sayfası gösterilir ve bitişte indirme sayfası açılır).
- T-Bridge: mevcut NSIS kurulumu pakete gömülür, `{tmp}`'e açılıp `/S` ile sessiz çalıştırılır. Kaldırması T-Bridge'in kendi kaldırıcısındandır (Suite kaldırıcısı kaldırmaz).
- Suite ve tek uygulama kurulumları ayrı `AppId` kullanır; aynı klasöre kurarlar. İkisini birden kurmak gerekmez.

## build.ps1 ve CI
```powershell
# Varsayılan: uygulama depolarını OKUR, geçici staging'e publish eder, hepsini derler
installer\build.ps1
# CI: hazır publish klasörleri / sürüm
installer\build.ps1 -RamCashDir D:\p\ramcash -DownloaderDir D:\p\dl -DesktreeDir D:\p\dt `
  -ExtensionDir D:\src\T-Downloader\extension -BridgeSetup D:\p\T-Bridge_0.3.1_x64-setup.exe `
  -Version 1.2.0 [-ViewerUrl https://...] [-Skip suite,desktree]
```
Varsayılan publish: `dotnet publish -c Release -r win-x64 --self-contained false -o <staging>` (Downloader için App + Host aynı klasöre). `ISCC.exe` otomatik bulunur ya da `-Iscc`. Doğrudan ISCC: `/DSourceDir=…` (tek uygulama) ya da `/DRamCashDir /DDownloaderDir /DDeskTreeDir /DBridgeSetup /DAppVersion /DViewerUrl /DOutDir`. Çıktılar: `installer/out/<Ad>-Setup-<sürüm>.exe`, `T-Suite-Setup-<sürüm>.exe`. CI'da Inno kurulumu: `winget install JRSoftware.InnoSetup` ya da `choco install innosetup`.

## Bilinen eksikler / notlar
- Kurulumlar çalıştırılarak denenmedi (yalnız derleme). .NET indirme/kurma yolu ve Restart Manager davranışı gerçek makinede sınanmalı.
- Kod imzası yok (SmartScreen uyarısı) — SignPath başvurusu ayrı iş.
- Viewer sayfası URL'i varsayılan yer tutucudur (`ViewerUrl`); yayın deposu belli olunca `-ViewerUrl` ile verilir ve `tools/make_wizard_images.py <url>` ile QR yenilenir.
- `dotnet publish` uygulama depolarındaki `bin/obj` klasörlerini günceller (kaynak dosyalara dokunmaz).
