# T-Suite SDK

For apps that ship the updater beside their executable, import
`build/TSuite.Updater.targets` instead of adding a `ProjectReference` to
`TSuite.Updater`. The targets populate both build and publish output with the
framework-dependent updater files. See [docs/INTEGRATION.md](docs/INTEGRATION.md)
and [samples/UpdaterHostSample](samples/UpdaterHostSample).
