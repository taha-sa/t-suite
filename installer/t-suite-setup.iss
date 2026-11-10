; T-Suite Kurulum: bilesen secmeli (T-Bridge, T-Downloader, T-RamCash, T-DeskTree, T-Desk Viewer = Telefon icin).
; Yollar parametredir (ISCC /D...): RamCashDir DownloaderDir DeskTreeDir BridgeSetup AppVersion OutDir ViewerUrl
#define SuiteSetup
#include "apps.iss.inc"
#ifndef OutDir
  #define OutDir AddBackslash(SourcePath) + "out"
#endif
#ifndef RamCashDir
  #define RamCashDir AddBackslash(SourcePath) + "staging\t-ramcash"
#endif
#ifndef DownloaderDir
  #define DownloaderDir AddBackslash(SourcePath) + "staging\t-downloader"
#endif
#ifndef DeskTreeDir
  #define DeskTreeDir AddBackslash(SourcePath) + "staging\t-desktree"
#endif
#ifndef BridgeSetup
  #define BridgeSetup "C:\Users\hp\Projects\kopru\dist-release\0.3.1\T-Bridge_0.3.1_x64-setup.exe"
#endif
#define SuiteIcon AddBackslash(SourcePath) + "..\design\icons\out\t-suite\app.ico"
#define IconsDir AddBackslash(SourcePath) + "..\design\icons\out\"
#if FileExists(BridgeSetup)
  #define HaveBridge
#else
  #pragma warning "T-Bridge setup bulunamadi, bilesen derlemeye dahil edilmiyor: BridgeSetup"
#endif

[Setup]
AppId={#SuiteGuid}
AppName=T-Suite
AppVersion={#AppVersion}
AppVerName=T-Suite {#AppVersion}
AppPublisher=T-Suite
; Duzen sabit: %LOCALAPPDATA%\Programs\T-Suite\<Ad>\ (T-Bridge otomatik baslatma yedegi bunu bekler)
DefaultDirName={localappdata}\Programs\T-Suite
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutDir}
OutputBaseFilename=T-Suite-Setup-{#AppVersion}
SetupIconFile={#SuiteIcon}
UninstallDisplayIcon={app}\t-suite.ico
UninstallDisplayName=T-Suite
WizardStyle=modern
WizardImageFile={#SourcePath}assets\t-suite-large.bmp,{#SourcePath}assets\t-suite-large@2x.bmp
WizardSmallImageFile={#SourcePath}assets\t-suite-small.bmp,{#SourcePath}assets\t-suite-small@2x.bmp
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=auto
UsePreviousLanguage=no
CloseApplications=yes
RestartApplications=no
AppMutex={#RC_Mutex},{#DL_Mutex},{#DT_Mutex}

[Types]
Name: "full"; Description: "{code:FullName}"
Name: "custom"; Description: "{code:CustomName}"; Flags: iscustom

[Components]
#ifdef HaveBridge
Name: "bridge"; Description: "{cm:CompBridge}"; Types: full
#endif
Name: "downloader"; Description: "{cm:CompDownloader}"; Types: full
Name: "ramcash"; Description: "{cm:CompRamCash}"; Types: full
Name: "desktree"; Description: "{cm:CompDeskTree}"; Types: full
Name: "viewer"; Description: "{cm:CompViewer}"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup_ramcash"; Description: "{cm:TaskStartup} - {#RC_Name}"; GroupDescription: "{cm:AdditionalIcons}"; Components: ramcash; Flags: checkedonce
Name: "startup_downloader"; Description: "{cm:TaskStartup} - {#DL_Name}"; GroupDescription: "{cm:AdditionalIcons}"; Components: downloader; Flags: checkedonce
Name: "startup_desktree"; Description: "{cm:TaskStartup} - {#DT_Name}"; GroupDescription: "{cm:AdditionalIcons}"; Components: desktree; Flags: unchecked

[Files]
Source: "{#SuiteIcon}"; DestDir: "{app}"; DestName: "t-suite.ico"; Flags: ignoreversion
Source: "{#RamCashDir}\*"; DestDir: "{app}\{#RC_Name}"; Excludes: "*.pdb"; Components: ramcash; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#IconsDir}t-ramcash\app.ico"; DestDir: "{app}\{#RC_Name}"; Components: ramcash; Flags: ignoreversion
Source: "{#DownloaderDir}\*"; DestDir: "{app}\{#DL_Name}"; Excludes: "*.pdb,extension\*"; Components: downloader; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DownloaderDir}\extension\*"; DestDir: "{app}\{#DL_Name}\extension"; Components: downloader; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#IconsDir}t-downloader\app.ico"; DestDir: "{app}\{#DL_Name}"; Components: downloader; Flags: ignoreversion
Source: "{#DeskTreeDir}\*"; DestDir: "{app}\{#DT_Name}"; Excludes: "*.pdb"; Components: desktree; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#IconsDir}t-desktree\app.ico"; DestDir: "{app}\{#DT_Name}"; Components: desktree; Flags: ignoreversion
#ifdef HaveBridge
Source: "{#BridgeSetup}"; DestDir: "{tmp}"; DestName: "T-Bridge-setup.exe"; Components: bridge; Flags: deleteafterinstall
#endif
Source: "{#SourcePath}assets\viewer-qr.bmp"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\T-Suite\{#RC_Name}"; Filename: "{app}\{#RC_Name}\{#RC_Exe}"; IconFilename: "{app}\{#RC_Name}\app.ico"; Components: ramcash
Name: "{autoprograms}\T-Suite\{#DL_Name}"; Filename: "{app}\{#DL_Name}\{#DL_Exe}"; IconFilename: "{app}\{#DL_Name}\app.ico"; Components: downloader
Name: "{autoprograms}\T-Suite\{#DT_Name}"; Filename: "{app}\{#DT_Name}\{#DT_Exe}"; IconFilename: "{app}\{#DT_Name}\app.ico"; Components: desktree
Name: "{autodesktop}\{#RC_Name}"; Filename: "{app}\{#RC_Name}\{#RC_Exe}"; IconFilename: "{app}\{#RC_Name}\app.ico"; Components: ramcash; Tasks: desktopicon
Name: "{autodesktop}\{#DL_Name}"; Filename: "{app}\{#DL_Name}\{#DL_Exe}"; IconFilename: "{app}\{#DL_Name}\app.ico"; Components: downloader; Tasks: desktopicon
Name: "{autodesktop}\{#DT_Name}"; Filename: "{app}\{#DT_Name}\{#DT_Exe}"; IconFilename: "{app}\{#DT_Name}\app.ico"; Components: desktree; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RC_Run}"; ValueData: """{app}\{#RC_Name}\{#RC_Exe}"" {#RC_Args}"; Components: ramcash; Tasks: startup_ramcash; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#RC_Run}"; Components: ramcash; Tasks: not startup_ramcash; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#DL_Run}"; ValueData: """{app}\{#DL_Name}\{#DL_Exe}"" {#DL_Args}"; Components: downloader; Tasks: startup_downloader; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#DL_Run}"; Components: downloader; Tasks: not startup_downloader; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#DT_Run}"; ValueData: """{app}\{#DT_Name}\{#DT_Exe}"" {#DT_Args}"; Components: desktree; Tasks: startup_desktree; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#DT_Run}"; Components: desktree; Tasks: not startup_desktree; Flags: deletevalue
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{userappdata}\T-Downloader\native-host\{#HostName}.json"; Components: downloader; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{userappdata}\T-Downloader\native-host\{#HostName}.json"; Components: downloader; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{userappdata}\T-Downloader\native-host\{#HostName}.json"; Components: downloader; Flags: uninsdeletekey

[UninstallDelete]
Type: files; Name: "{userappdata}\T-Downloader\native-host\{#HostName}.json"
Type: dirifempty; Name: "{userappdata}\T-Downloader\native-host"
Type: filesandordirs; Name: "{app}"

[Run]
#ifdef HaveBridge
Filename: "{tmp}\T-Bridge-setup.exe"; Parameters: "/S"; StatusMsg: "{cm:BridgeRunning}"; Components: bridge; Flags: waituntilterminated runasoriginaluser
#endif
Filename: "{app}\{#RC_Name}\{#RC_Exe}"; Description: "{cm:LaunchApp,{#RC_Name}}"; Components: ramcash; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#DT_Name}\{#DT_Exe}"; Description: "{cm:LaunchApp,{#DT_Name}}"; Components: desktree; Flags: nowait postinstall skipifsilent unchecked
Filename: "{win}\explorer.exe"; Parameters: """{app}\{#DL_Name}\extension"""; Description: "{cm:ExtHelp}"; Components: downloader; Flags: nowait postinstall skipifsilent
Filename: "{#ViewerUrl}"; Description: "{cm:CompViewer}"; Components: viewer; Flags: shellexec nowait postinstall skipifsilent

#include "common.iss.inc"

[Code]
var
  ViewerPage: TWizardPage;

function FullName(Param: String): String;
begin
  Result := SetupMessage(msgFullInstallation);
end;

function CustomName(Param: String): String;
begin
  Result := SetupMessage(msgCustomInstallation);
end;

procedure OpenViewerUrl(Sender: TObject);
var
  Rc: Integer;
begin
  ShellExec('open', '{#ViewerUrl}', '', '', SW_SHOWNORMAL, ewNoWait, Rc);
end;

procedure InitializeWizard;
var
  Img: TBitmapImage;
  Txt, Lnk: TNewStaticText;
begin
  ViewerPage := CreateCustomPage(wpSelectComponents, CustomMessage('ViewerTitle'), CustomMessage('ViewerDesc'));
  ExtractTemporaryFile('viewer-qr.bmp');
  Txt := TNewStaticText.Create(ViewerPage);
  Txt.Parent := ViewerPage.Surface;
  Txt.Caption := CustomMessage('ViewerText');
  Txt.Left := 0;
  Txt.Top := 0;
  Txt.AutoSize := True;
  Img := TBitmapImage.Create(ViewerPage);
  Img.Parent := ViewerPage.Surface;
  Img.Bitmap.LoadFromFile(ExpandConstant('{tmp}\viewer-qr.bmp'));
  Img.Stretch := True;
  Img.Left := 0;
  Img.Top := ScaleY(28);
  Img.Width := ScaleX(150);
  Img.Height := ScaleY(150);
  Lnk := TNewStaticText.Create(ViewerPage);
  Lnk.Parent := ViewerPage.Surface;
  Lnk.Caption := '{#ViewerUrl}';
  Lnk.Cursor := crHand;
  Lnk.Font.Color := clBlue;
  Lnk.Font.Style := [fsUnderline];
  Lnk.Left := 0;
  Lnk.Top := ScaleY(190);
  Lnk.AutoSize := True;
  Lnk.OnClick := @OpenViewerUrl;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ViewerPage.ID then
    Result := not WizardIsComponentSelected('viewer');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsComponentSelected('ramcash') then
      WriteManifestStub('{#RC_Id}', '{#RC_Name}', ExpandConstant('{app}\{#RC_Name}\{#RC_Exe}'));
    if WizardIsComponentSelected('downloader') then
    begin
      WriteManifestStub('{#DL_Id}', '{#DL_Name}', ExpandConstant('{app}\{#DL_Name}\{#DL_Exe}'));
      WriteHostManifest(ExpandConstant('{app}\{#DL_Name}\TDownloader.Host.exe'));
    end;
    if WizardIsComponentSelected('desktree') then
      WriteManifestStub('{#DT_Id}', '{#DT_Name}', ExpandConstant('{app}\{#DT_Name}\{#DT_Exe}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Del: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    Del := WantDeleteSettings;
    CleanApp('{#RC_Id}', '{#RC_Run}', ExpandConstant('{app}\{#RC_Name}'), ExpandConstant('{#RC_Settings}'), Del);
    CleanApp('{#DL_Id}', '{#DL_Run}', ExpandConstant('{app}\{#DL_Name}'), ExpandConstant('{#DL_Settings}'), Del);
    CleanApp('{#DT_Id}', '{#DT_Run}', ExpandConstant('{app}\{#DT_Name}'), ExpandConstant('{#DT_Settings}'), Del);
  end;
end;

