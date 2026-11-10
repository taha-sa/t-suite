; T-DeskTree kurulumu. Derleme: ISCC t-desktree.iss /DSourceDir=<publish klasoru> /DAppVersion=1.1.0 (build.ps1 yapar)
#include "apps.iss.inc"
#define AppName DT_Name
#define AppId DT_Id
#define AppExe DT_Exe
#define AppGuid DT_Guid
#define RunValue DT_Run
#define RunArgs DT_Args
#define AppSettings DT_Settings
#define AppMutex DT_Mutex
#define StartupFlags "unchecked"
#include "app.iss.inc"
