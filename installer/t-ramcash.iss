; T-RamCash kurulumu. Derleme: ISCC t-ramcash.iss /DSourceDir=<publish klasoru> /DAppVersion=1.1.0 (build.ps1 yapar)
#include "apps.iss.inc"
#define AppName RC_Name
#define AppId RC_Id
#define AppExe RC_Exe
#define AppGuid RC_Guid
#define RunValue RC_Run
#define RunArgs RC_Args
#define AppSettings RC_Settings
#define AppMutex RC_Mutex
#define StartupFlags "checkedonce"
#include "app.iss.inc"
