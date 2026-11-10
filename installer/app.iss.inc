; ---- Tek uygulama kurulum sablonu. Onceden tanimlanmasi gerekenler:
;  AppName AppId AppExe AppGuid RunValue RunArgs AppSettings AppMutex StartupFlags(checkedonce|unchecked)
;  Opsiyonel: WithHost (T-Downloader), SourceDir, AppVersion
#include "apps.iss.inc"
#ifndef SourceDir
  #define SourceDir AddBackslash(SourcePath) + "staging\" + AppId
#endif
#ifndef OutDir
  #define OutDir AddBackslash(SourcePath) + "out"
#endif
#define IconFile AddBackslash(SourcePath) + "..\design\icons\out\" + AppId + "\app.ico"

[Setup]
AppId={#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=T-Suite
DefaultDirName={localappdata}\Programs\T-Suite\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile={#SourcePath}assets\{#AppId}-large.bmp,{#SourcePath}assets\{#AppId}-large@2x.bmp
WizardSmallImageFile={#SourcePath}assets\{#AppId}-small.bmp,{#SourcePath}assets\{#AppId}-small@2x.bmp
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=auto
UsePreviousLanguage=no
CloseApplications=yes
RestartApplications=no
AppMutex={#AppMutex}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: {#StartupFlags}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,extension\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#IconFile}"; DestDir: "{app}"; Flags: ignoreversion
#ifdef WithHost
Source: "{#SourceDir}\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
#endif

[Icons]
Name: "{autoprograms}\T-Suite\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Registry]
; Uygulamalarin kendi yazdigi deger adi/argumanlariyla birebir ayni (bkz. docs/INSTALLER.md)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"" {#RunArgs}"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#RunValue}"; Tasks: not startup; Flags: deletevalue
#ifdef WithHost
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{userappdata}\T-Downloader\native-host\{#HostName}.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{userappdata}\T-Downloader\native-host\{#HostName}.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{userappdata}\T-Downloader\native-host\{#HostName}.json"; Flags: uninsdeletekey
#endif

[UninstallDelete]
#ifdef WithHost
Type: files; Name: "{userappdata}\T-Downloader\native-host\{#HostName}.json"
Type: dirifempty; Name: "{userappdata}\T-Downloader\native-host"
#endif
Type: filesandordirs; Name: "{app}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp,{#AppName}}"; Flags: nowait postinstall skipifsilent
#ifdef WithHost
Filename: "{win}\explorer.exe"; Parameters: """{app}\extension"""; Description: "{cm:ExtHelp}"; Flags: nowait postinstall skipifsilent
#endif

#include "common.iss.inc"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteManifestStub('{#AppId}', '{#AppName}', ExpandConstant('{app}\{#AppExe}'));
#ifdef WithHost
    WriteHostManifest(ExpandConstant('{app}\TDownloader.Host.exe'));
#endif
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CleanApp('{#AppId}', '{#RunValue}', ExpandConstant('{app}'), ExpandConstant('{#AppSettings}'), WantDeleteSettings);
end;

