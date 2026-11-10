; ---- T-Suite ortak sabitler / shared constants (UTF-8) ----
#ifndef AppsIncluded
#define AppsIncluded
#define RC_Name "T-RamCash"
#define RC_Id "t-ramcash"
#define RC_Exe "T-RamCash.exe"
#define RC_Run "T-RamCash"
#define RC_Args "--tray"
#define RC_Settings "{userappdata}\T-Desk\T-RamCash"
#define RC_Guid "{{6F0C8E52-4B1A-4C0E-9A57-2D5B7A3C1001}"
#define RC_Mutex "Global\TRamCash_SingleInstance_Mutex_998877"

#define DL_Name "T-Downloader"
#define DL_Id "t-downloader"
#define DL_Exe "TDownloader.exe"
#define DL_Run "T-Downloader"
#define DL_Args "--background"
#define DL_Settings "{userappdata}\T-Downloader"
#define DL_Guid "{{6F0C8E52-4B1A-4C0E-9A57-2D5B7A3C1002}"
#define DL_Mutex "Local\TDownloader.SingleInstance"

#define DT_Name "T-DeskTree"
#define DT_Id "t-desktree"
#define DT_Exe "T-DeskTree.exe"
#define DT_Run "T-DeskTree"
#define DT_Args "--background"
#define DT_Settings "{userappdata}\AmberTree"
#define DT_Guid "{{6F0C8E52-4B1A-4C0E-9A57-2D5B7A3C1003}"
#define DT_Mutex "Local\T-DeskTree.Single"

#define SuiteGuid "{{6F0C8E52-4B1A-4C0E-9A57-2D5B7A3C1000}"
#define DotNetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define HostName "com.tdownloader.host"
#ifndef ExtensionId
  #define ExtensionId "lgceenlgaaekpbeifoookmmkjipmcboc"
#endif
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef ViewerUrl
  #define ViewerUrl "https://github.com/taha-sa/tdesk-viewer-releases/releases/latest"
#endif
#endif
