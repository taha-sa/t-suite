# App CI and releases

These reusable workflows run in each **app repository**, with Windows and .NET 10.
They check out the caller at `app/` and `taha-sa/t-suite` at sibling `t-suite/`.
All builds receive the absolute SDK location through `TSuiteRoot`.
The SDK checkout follows its default branch; pin the workflow `uses` references to
a reviewed commit SHA for controlled workflow updates.

In the private SDK repository's **Settings → Actions → General → Access**, allow
the intended app repositories to use its reusable workflows. This access is
separate from the token needed to check out the SDK source.

## Push and PR CI

Add `.github/workflows/ci.yml` in the app repository:

```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
permissions:
  contents: read
jobs:
  ci:
    uses: taha-sa/t-suite/.github/workflows/app-ci.yml@main
    with:
      app_project: src/MyApp/MyApp.csproj
      test_projects: 'tests/**/*.csproj'
      dotnet_version: '10.0.x'
    secrets:
      TSUITE_READ_TOKEN: ${{ secrets.TSUITE_READ_TOKEN }}
```

`app_project` is relative to the app repository. `test_projects` accepts a glob
(`*`, `?`, `**`) or comma/newline separated paths/globs. For example:

```yaml
      test_projects: |
        tests/MyApp.Tests/MyApp.Tests.csproj
        integration/**/*.csproj
```

Use `test_projects: ''` explicitly for apps without tests. A nonempty selection
that matches no projects fails CI. CI restores and builds Release, runs selected
tests, then publishes `win-x64` with `--self-contained false`. Download the
`app-win-x64` artifact from the workflow run. Fork PRs do not receive repository
secrets, so this private SDK workflow needs a trusted run with token access;
do not use `pull_request_target` to execute untrusted PR code with secrets.

## Tag releases

Copy `tools/release-template/release.ps1` to the app repository root and configure
its application settings (`AppId`, `Project`, `ExeName`, `ArtifactAs`). Existing
copies must be updated to accept `-KeyPath`, `-ApplicationId`, `-Channel`, and
`-ReleaseRepo`, and to use the `TSuiteRoot` environment variable. The project must
also honor the `TSuiteRoot` MSBuild property for SDK references/imports.

Add `.github/workflows/release.yml` in the app repository:

```yaml
name: Release
on:
  push:
    tags: ['v*']
permissions:
  contents: read
jobs:
  release:
    uses: taha-sa/t-suite/.github/workflows/app-release.yml@main
    with:
      app_id: my-app
      release_repo: taha-sa/my-app-releases
      version: ${{ github.ref_name }}
      channel: ${{ contains(github.ref_name, '-beta') && 'beta' || 'stable' }}
    secrets:
      UPDATE_SIGNING_KEY: ${{ secrets.UPDATE_SIGNING_KEY }}
      RELEASE_TOKEN: ${{ secrets.RELEASE_TOKEN }}
      TSUITE_READ_TOKEN: ${{ secrets.TSUITE_READ_TOKEN }}
```

`version` accepts `1.2.3` or `v1.2.3` (also prerelease versions such as
`v1.2.3-beta.1`). `channel` accepts only `stable` or `beta`, defaulting to `stable`.
Beta releases use `gh release create --prerelease`; both channels include their
channel in the signed manifest (see channel publishing below). The template uses generated GitHub release notes
when `-Notes` is omitted; supply notes locally as before if desired.

The workflow writes the PEM to a unique file under `RUNNER_TEMP`, passes its
path to the app's script, and removes it in `finally`. It invokes the script
with `-Publish`; the script signs the artifact and runs `gh release create`
against `release_repo`, authenticating through `GH_TOKEN=RELEASE_TOKEN`.
The signing key must match the app's configured update public key, and `app_id`
must match the updater's application ID. The artifact URL uses `release_repo`.

## Secrets and release approval

Configure these Actions secrets in the **app repository** (or as organization
secrets granted to it):

| Secret | Value and access |
| --- | --- |
| `TSUITE_READ_TOKEN` | Token with Contents read access to the private `taha-sa/t-suite` repository. |
| `UPDATE_SIGNING_KEY` | Full private signing PEM, including BEGIN/END lines and newlines. |
| `RELEASE_TOKEN` | Token with Contents write access to the target `release_repo`. |

In the app repository, open **Settings → Environments → New environment**, name
it exactly `release`, and add **Required reviewers** under its protection rules.
Enable prevention of self review if desired, and restrict deployment tags to
your release tags. These controls must be available for your GitHub plan and
repository visibility. The reusable release job declares `environment: release`,
so approval gates the job before checkout, signing, and publication.

The examples pass repository secrets explicitly. Environment secrets of the
same name take precedence inside the release job. Keep the SDK read token
available to CI as well.

Local usage stays compatible:

```powershell
./release.ps1 1.2.3 'Release notes'                  # builds/signs only
./release.ps1 1.2.3 'Release notes' -Publish         # publishes a stable release
./release.ps1 1.2.4-beta.1 -KeyPath C:/keys/app.pem -Channel beta -Publish
```

Without `-KeyPath`, the template still uses
`$env:USERPROFILE/.t-suite/keys/<AppId>.pem`. Without `TSuiteRoot`, it retains its
existing local SDK directory default. `-NoPublish` still overrides `-Publish`.

## Blocking a bad release and channel publishing

Update existing app copies of release.ps1 to the current template. Beta releases
write latest-beta.json and upload that pointer onto the current stable release,
since GitHub releases/latest excludes prereleases. Publish a stable baseline first.
Stable releases write both latest.json and latest-beta.json with identical signed
contents so beta clients also receive stable releases. Both signatures are generated.

Publish a new latest.json pointing to a newer safe artifact, with `blocked_versions`
listing the bad release and `min_version` set to the oldest supported version.
For example, to replace bad 1.2.3 with safe 1.2.4:

```powershell
./release.ps1 1.2.4 'Emergency fix' -MinVersion 1.2.4 -BlockedVersions @('1.2.3') -Publish
```

The reusable workflow accepts optional `min_version` and comma-separated
`blocked_versions` inputs and passes them to the template. For an existing safe
release, regenerate the manifest using the exact artifact hash and sign command in
PROTOCOL §8.2, then upload/replace latest.json and latest-beta.json on the current
stable release. Preserve both signatures; editing policy fields requires resigning
signature2. Never point the manifest at a blocked artifact. Keep the v1 signature
compatible with the embedded key in installed clients (see §8.1 rotation).

Updated clients below the minimum or running a blocked version cannot defer/skip
the offered safe update. Installed 1.1.0 clients still verify v1 and update normally,
but cannot enforce these new policies until upgraded. Manifest publication takes
effect on the next network check (normally up to 24 hours); it cannot remotely stop
an offline client or a download that has already started.