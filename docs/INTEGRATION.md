# App integration

Apps that need `TSuite.Updater.exe` beside their own executable should import the
SDK targets file. Do not add a `ProjectReference` to `src/TSuite.Updater`.

```xml
<Import Project="..\..\build\TSuite.Updater.targets" />
```

The targets build the updater separately as a framework-dependent, non-single-file
application using its own target framework and the host app's runtime identifier (if any). After
`Build` and `Publish`, they copy its `TSuite.Updater.exe`, DLL, `.deps.json`, and
`.runtimeconfig.json` (plus any other updater output) beside the host output.
This avoids the self-contained and duplicate-output conflicts caused by an exe
`ProjectReference`, including NETSDK1151 and NETSDK1152.

For example:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

See `samples/UpdaterHostSample` for a complete minimal host.

## Hata raporu

Reference `src/TSuite.Diagnostics/TSuite.Diagnostics.csproj` (`net10.0-windows`, BCL only).
It exposes both desktop helpers; WPF hosts can use the data APIs without showing a WinForms dialog.
`UpdateTheme` keeps its `TSuite.Update` namespace and now lives in Diagnostics, so the same
theme instance works with both `UpdateCard` and `CrashReportDialog`.

WinForms: put these five lines on the STA UI thread after `ApplicationConfiguration.Initialize()`
and before `Application.Run(...)`. Import `TSuite.Diagnostics` and `TSuite.Update`.

```csharp
CrashReporter.Install("t-ramcash", "T-RamCash", "1.0.0", "taha-sa/t-ramcash-releases", UpdateTheme.Dark);
CrashReporter.InstallWinForms();
Log.Info("Application starting");
if (CrashReporter.GetPendingReport() is { } report)
    using (var dialog = new CrashReportDialog(report, UpdateTheme.Dark)) dialog.ShowDialog();
```

Use `t-downloader`, `T-Downloader`, and its releases repository for T-Downloader.
The host owns WinForms exception policy: the helper does not change `UnhandledExceptionMode`.
WinForms ThreadException normally keeps the application running; explicitly exit in the host
if that is its crash policy. Neither the AppDomain nor WPF helper suppresses fatal exceptions;
the task helper does not call `SetObserved()`.

WPF: use these five lines in `App.OnStartup`, with `using TSuite.Diagnostics;`.
`ShowCrashPreview` is a host-provided XAML dialog taking the report, full preview, and issue URL.
Keep `crashHook` in an App field and dispose it on exit; attaching again adds another subscription.

```csharp
CrashReporter.Install("t-desktree", "T-DeskTree", "1.0.0", "taha-sa/t-desktree-releases");
crashHook = CrashReporter.InstallWpf(Dispatcher);
Log.Info("Application starting");
if (CrashReporter.GetPendingReport() is { } report)
    ShowCrashPreview(report, CrashReporter.GetReportText(report), CrashReporter.BuildIssueUrl(report));
```

In that XAML dialog, show the preview before opening the URL with `UseShellExecute = true`.
Call `MarkReported(report)` after successfully opening the browser, or `MarkDismissed(report)`
when closing without sending. Opening the browser prepares an issue; the user submits it on GitHub.
The WinForms dialog does this automatically and offers Copy, Enter/Esc/Tab, and a preference checkbox.
The correctly encoded Turkish caption is “Uygulama beklenmedik şekilde kapandı”.

`CrashReporter.AutomaticallyPrepareReports` is the per-app “Hata raporlarını otomatik hazırla”
setting (default true), stored in `%LOCALAPPDATA%\T-Suite\diagnostics.json` as app-ID/boolean entries.
When false, exceptions are only logged and `GetPendingReport()` returns null. A previously pending
report remains available if the setting is enabled again. Installation is once per process/app.

Logs live in `%LOCALAPPDATA%\T-Suite\logs\<appId>\app.log`, with `.1` and `.2` backups,
each at most 1 MiB. Writes are synchronous, locked, scrubbed, and best effort; oversized entries
are truncated. `Log.Info/Warn/Error` are no-ops before installation. Use a single writing process
per app ID; rotation and preference read/modify/write are synchronized within the process.
Crash JSON files and the `pending-crash.json` marker are atomically replaced; completing a report
clears only its marker and retains local crash files. Hosts may apply their own retention policy.
Capture includes aggregate/inner exceptions (bounded depth), environment, uptime and the latest
200 log lines across rotation. Disk failures never escape exception handlers. Native fail-fast,
stack-overflow, power loss and process termination cannot reliably be captured.

Scrubbing masks identities, user-profile paths, email, IPs, URL credentials/query/fragment,
named secrets and long token runs before persistence and again before preview/URL construction.
`new Scrubber(maskGuids: true)` additionally masks GUIDs; GUID masking is off by default.
Filtering is best effort, so users should inspect the full preview before sharing a public report.
Issue URLs are percent-encoded and kept below 7000 characters, retaining the top of the stack;
Copy and `GetReportText` preserve the full scrubbed report.
