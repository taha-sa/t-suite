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
