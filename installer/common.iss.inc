; ---- Ortak: diller, mesajlar, .NET kontrolu, manifest, temizlik (UTF-8) ----
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
turkish.DotNetDlTitle=.NET 10 Desktop Runtime
turkish.DotNetDlDesc=Gerekli bileşen indiriliyor (Microsoft)...
turkish.DotNetInstalling=.NET 10 Desktop Runtime kuruluyor. Yönetici izni istenebilir...
turkish.DotNetOffline=.NET 10 Desktop Runtime bulunamadı ve indirilemedi (internet bağlantısı yok olabilir).%n%nİnternete bağlanıp kurulumu yeniden çalıştırın ya da https://dotnet.microsoft.com/download/dotnet/10.0 adresinden "Desktop Runtime (x64)" kurun.
turkish.DotNetFail=.NET 10 Desktop Runtime kurulamadı (kod %1). Lütfen https://dotnet.microsoft.com/download/dotnet/10.0 adresinden elle kurup tekrar deneyin.
turkish.TaskStartup=Windows ile &başlat
turkish.DeleteSettingsPrompt=Kullanıcı ayarları ve günlükler de silinsin mi?%n%n"Hayır" derseniz ayarlarınız korunur (yeniden kurunca devam eder).
turkish.ExtHelp=Tarayıcı eklentisi klasörünü aç (chrome://extensions veya edge://extensions > Geliştirici modu > "Paketlenmemiş öğe yükle")
turkish.CompBridge=T-Bridge (PC - telefon köprüsü)
turkish.CompDownloader=T-Downloader (indirme yöneticisi + tarayıcı eklentisi)
turkish.CompRamCash=T-RamCash (bellek temizleyici)
turkish.CompDeskTree=T-DeskTree (disk kullanımı haritası)
turkish.CompViewer=T-Desk Viewer - Telefon için (Android; kurulum sonrası indirme sayfası açılır)
turkish.ViewerTitle=T-Desk Viewer - Telefon için
turkish.ViewerDesc=Android uygulaması telefona kurulur, bilgisayara değil.
turkish.ViewerText=Telefonunuzla QR kodu okutun veya bağlantıyı açın:
turkish.BridgeRunning=T-Bridge kuruluyor...
turkish.BridgeFail=T-Bridge kurulumu tamamlanamadı; diğer bileşenler kuruldu. T-Bridge'i ayrıca kurabilirsiniz.
turkish.LaunchApp=%1 uygulamasını başlat
english.DotNetDlTitle=.NET 10 Desktop Runtime
english.DotNetDlDesc=Downloading a required component (Microsoft)...
english.DotNetInstalling=Installing .NET 10 Desktop Runtime. You may be asked for administrator permission...
english.DotNetOffline=.NET 10 Desktop Runtime was not found and could not be downloaded (you may be offline).%n%nConnect to the internet and run Setup again, or install the "Desktop Runtime (x64)" from https://dotnet.microsoft.com/download/dotnet/10.0.
english.DotNetFail=.NET 10 Desktop Runtime could not be installed (code %1). Please install it manually from https://dotnet.microsoft.com/download/dotnet/10.0 and try again.
english.TaskStartup=&Start with Windows
english.DeleteSettingsPrompt=Also delete user settings and logs?%n%nIf you choose No your settings are kept (they are reused if you reinstall).
english.ExtHelp=Open the browser extension folder (chrome://extensions or edge://extensions > Developer mode > "Load unpacked")
english.CompBridge=T-Bridge (PC to phone bridge)
english.CompDownloader=T-Downloader (download manager + browser extension)
english.CompRamCash=T-RamCash (memory cleaner)
english.CompDeskTree=T-DeskTree (disk usage map)
english.CompViewer=T-Desk Viewer - For your phone (Android; the download page opens after setup)
english.ViewerTitle=T-Desk Viewer - For your phone
english.ViewerDesc=The Android app is installed on your phone, not on this PC.
english.ViewerText=Scan the QR code with your phone or open the link:
english.BridgeRunning=Installing T-Bridge...
english.BridgeFail=T-Bridge setup did not finish; the other components were installed. You can install T-Bridge separately.
english.LaunchApp=Launch %1
german.DotNetDlTitle=.NET 10 Desktop Runtime
german.DotNetDlDesc=Eine erforderliche Komponente wird heruntergeladen (Microsoft)...
german.DotNetInstalling=.NET 10 Desktop Runtime wird installiert. Eventuell wird eine Administratorbestätigung verlangt...
german.DotNetOffline=Die .NET 10 Desktop Runtime wurde nicht gefunden und konnte nicht heruntergeladen werden (eventuell offline).%n%nStellen Sie eine Internetverbindung her und starten Sie das Setup erneut, oder installieren Sie die "Desktop Runtime (x64)" von https://dotnet.microsoft.com/download/dotnet/10.0.
german.DotNetFail=Die .NET 10 Desktop Runtime konnte nicht installiert werden (Code %1). Bitte installieren Sie sie manuell von https://dotnet.microsoft.com/download/dotnet/10.0 und versuchen Sie es erneut.
german.TaskStartup=Mit &Windows starten
german.DeleteSettingsPrompt=Benutzereinstellungen und Protokolle ebenfalls löschen?%n%nBei "Nein" bleiben Ihre Einstellungen erhalten (sie werden bei einer Neuinstallation weiterverwendet).
german.ExtHelp=Ordner der Browser-Erweiterung öffnen (chrome://extensions oder edge://extensions > Entwicklermodus > "Entpackte Erweiterung laden")
german.CompBridge=T-Bridge (PC-Handy-Brücke)
german.CompDownloader=T-Downloader (Download-Manager + Browser-Erweiterung)
german.CompRamCash=T-RamCash (Speicherbereiniger)
german.CompDeskTree=T-DeskTree (Datenträger-Belegungskarte)
german.CompViewer=T-Desk Viewer - Für das Handy (Android; die Download-Seite öffnet sich nach dem Setup)
german.ViewerTitle=T-Desk Viewer - Für das Handy
german.ViewerDesc=Die Android-App wird auf dem Handy installiert, nicht auf diesem PC.
german.ViewerText=Scannen Sie den QR-Code mit dem Handy oder öffnen Sie den Link:
german.BridgeRunning=T-Bridge wird installiert...
german.BridgeFail=Die T-Bridge-Installation wurde nicht abgeschlossen; die übrigen Komponenten sind installiert. T-Bridge kann separat installiert werden.
german.LaunchApp=%1 starten

[Code]
const
  DotNetKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

function IsDotNetDesktop10Installed: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, DotNetKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 3) = '10.' then
      begin
        Result := True;
        Exit;
      end;
end;

function NeedsDotNet: Boolean;
begin
#ifdef SuiteSetup
  Result := WizardIsComponentSelected('ramcash') or WizardIsComponentSelected('downloader') or WizardIsComponentSelected('desktree');
#else
  Result := True;
#endif
end;

{ Indirir ve sessiz kurar. Hata durumunda yerellestirilmis mesaj dondurur, basariyda bos metin. }
function EnsureDotNet: String;
var
  Dp: TDownloadWizardPage;
  Exe: String;
  Rc: Integer;
begin
  Result := '';
  if (not NeedsDotNet) or IsDotNetDesktop10Installed then Exit;
  Exe := 'windowsdesktop-runtime-10-win-x64.exe';
  Dp := CreateDownloadPage(CustomMessage('DotNetDlTitle'), CustomMessage('DotNetDlDesc'), nil);
  Dp.Clear;
  Dp.Add('{#DotNetUrl}', Exe, '');
  Dp.Show;
  try
    try
      Dp.Download;
    except
      Result := CustomMessage('DotNetOffline');
      Exit;
    end;
    Dp.SetText(CustomMessage('DotNetInstalling'), '');
    Dp.SetProgress(0, 0);
    if not ShellExec('runas', ExpandConstant('{tmp}\' + Exe), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Rc) then
      Result := FmtMessage(CustomMessage('DotNetFail'), [IntToStr(Rc)])
    else if not ((Rc = 0) or (Rc = 3010) or (Rc = 1641)) then
      Result := FmtMessage(CustomMessage('DotNetFail'), [IntToStr(Rc)])
    else if not IsDotNetDesktop10Installed then
      Result := FmtMessage(CustomMessage('DotNetFail'), [IntToStr(Rc)]);
  finally
    Dp.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := EnsureDotNet;
end;

{ AppRegistry taslak manifesti (%LOCALAPPDATA%\T-Suite\apps\<id>.json). Var olani EZMEZ; uygulama ilk acilista kendisi tamamlar. }
procedure WriteManifestStub(Id, Name, ExePath: String);
var
  Dir, F, P: String;
  L: TArrayOfString;
begin
  Dir := ExpandConstant('{localappdata}\T-Suite\apps');
  F := Dir + '\' + Id + '.json';
  if FileExists(F) then Exit;
  ForceDirectories(Dir);
  P := ExePath;
  StringChangeEx(P, '\', '\\', True);
  SetArrayLength(L, 1);
  L[0] := '{"id":"' + Id + '","name":"' + Name + '","version":"{#AppVersion}","exePath":"' + P + '","pipeName":"TSuite.' + Id + '","events":[],"commands":[],"pid":0}';
  SaveStringsToUTF8FileWithoutBOM(F, L, False);
end;

{ Kaldirma: Run degeri + manifest (yalnizca bu kurulumun yolunu gosteriyorsa) + istege bagli ayarlar. }
procedure CleanApp(Id, RunValue, AppDir, SettingsDir: String; DelSettings: Boolean);
var
  Data, Json, Esc, Tail, Mf, Key: String;
  Raw: AnsiString;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Run';
  if RegQueryStringValue(HKCU, Key, RunValue, Data) then
    if Pos(Lowercase(AppDir), Lowercase(Data)) > 0 then
      RegDeleteValue(HKCU, Key, RunValue);
  if Id = 't-ramcash' then RegDeleteValue(HKCU, Key, 'MemReductClone');
  Mf := ExpandConstant('{localappdata}\T-Suite\apps\') + Id + '.json';
  if LoadStringFromFile(Mf, Raw) then
  begin
    Json := Lowercase(String(Raw));
    Esc := AppDir;
    StringChangeEx(Esc, '\', '\\', True);
    { Dosya UTF-8, Setup ANSI okur: ASCII olmayan kullanici adinda tam yol eslesmez; son iki klasore (T-Suite\Ad) dus. }
    Tail := ExtractFileName(ExtractFileDir(AppDir)) + '\' + ExtractFileName(AppDir);
    StringChangeEx(Tail, '\', '\\', True);
    if (Pos(Lowercase(Esc), Json) > 0) or (Pos(Lowercase(Tail), Json) > 0) then
      DeleteFile(Mf);
  end;
  if DelSettings and DirExists(AppDir) then
  begin
    DelTree(SettingsDir, True, True, True);
    DelTree(ExpandConstant('{localappdata}\T-Suite\logs\') + Id, True, True, True);
  end;
end;

function WantDeleteSettings: Boolean;
begin
  if ExpandConstant('{param:DELETESETTINGS|0}') = '1' then
    Result := True
  else if UninstallSilent then
    Result := False
  else
    Result := MsgBox(CustomMessage('DeleteSettingsPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

{ Native messaging host: manifest JSON + (HKCU kayitlari [Registry] bolumunde). }
procedure WriteHostManifest(HostExe: String);
var
  Dir, F, P: String;
  L: TArrayOfString;
begin
  Dir := ExpandConstant('{userappdata}\T-Downloader\native-host');
  F := Dir + '\{#HostName}.json';
  ForceDirectories(Dir);
  P := HostExe;
  StringChangeEx(P, '\', '\\', True);
  SetArrayLength(L, 9);
  L[0] := '{';
  L[1] := '  "name": "{#HostName}",';
  L[2] := '  "description": "T-Downloader native messaging host",';
  L[3] := '  "path": "' + P + '",';
  L[4] := '  "type": "stdio",';
  L[5] := '  "allowed_origins": [';
  L[6] := '    "chrome-extension://{#ExtensionId}/"';
  L[7] := '  ]';
  L[8] := '}';
  SaveStringsToUTF8FileWithoutBOM(F, L, False);
end;
