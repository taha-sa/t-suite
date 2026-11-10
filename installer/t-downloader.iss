; T-Downloader kurulumu. Derleme: ISCC t-downloader.iss /DSourceDir=<publish klasoru> /DAppVersion=1.1.0 (build.ps1 yapar)
#include "apps.iss.inc"
#define AppName DL_Name
#define AppId DL_Id
#define AppExe DL_Exe
#define AppGuid DL_Guid
#define RunValue DL_Run
#define RunArgs DL_Args
#define AppSettings DL_Settings
#define AppMutex DL_Mutex
#define StartupFlags "checkedonce"
#define WithHost
#include "app.iss.inc"
