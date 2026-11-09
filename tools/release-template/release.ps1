<#
  T-Suite release sablonu / release template. Her uygulama bunu kendi reposuna kopyalar ve
  yalnizca "UYGULAMA AYARLARI" bolumunu duzenler.

  Kullanim:  .\release.ps1 1.2.0 "Surum notlari"            # varsayilan: yerel build + imza, YAYINLAMAZ
             .\release.ps1 1.2.0 "Notlar" -Publish          # gh release create (kullanici onayi gerekir!)
  -NoPublish eski uyumluluk icin kabul edilir; varsayilan zaten yayinlamamaktir.

  Cikti: dist-release\<Artifact> + dist-release\latest.json
  Ozel anahtar: C:\Users\hp\.t-suite\keys\<AppId>.pem (repo DISINDA; asla commit edilmez)
#>
param(
  [Parameter(Mandatory = $true, Position = 0)][string]$Version,
  [Parameter(Position = 1)][string]$Notes = '',
  [switch]$Publish,
  [switch]$NoPublish,
  [string]$KeyPath,
  [string]$ApplicationId,
  [ValidateSet('stable', 'beta')][string]$Channel = 'stable',
  [string]$MinVersion,
  [string[]]$BlockedVersions = @(),
  [string]$ReleaseRepo = $env:TSUITE_RELEASE_REPO
)
$ErrorActionPreference = 'Stop'

# ===================== UYGULAMA AYARLARI =====================
$AppId      = 't-downloader'                                  # t-ramcash | t-desktree | ...
$Project    = 'src\TDownloader.App\TDownloader.App.csproj'    # publish edilecek proje
$ExeName    = 'TDownloader.App.exe'                            # publish ciktisindaki exe
$ArtifactAs = 'T-Downloader.exe'                               # surum dosyasi adi (.zip yazarsan klasor zip'lenir)
$Rid        = 'win-x64'
$SuiteRoot  = if ($env:TSuiteRoot) { $env:TSuiteRoot } else { 'C:\Users\hp\Projects\t-suite' }
$GhOwner    = 'taha-sa'
# =============================================================

if ($ApplicationId) { $AppId = $ApplicationId }

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Surum bicimi X.Y.Z olmali: $Version" }
$repo   = if ($ReleaseRepo) { $ReleaseRepo } else { "$GhOwner/$AppId-releases" }
$out    = Join-Path $PSScriptRoot 'dist-release'
$pub    = Join-Path $out 'pub'
$keyPem = if ($KeyPath) { (Resolve-Path -LiteralPath $KeyPath).Path } else { Join-Path $env:USERPROFILE ".t-suite\keys\$AppId.pem" }
if (-not (Test-Path $keyPem)) { throw "Ozel anahtar yok: $keyPem (once: dotnet run --project $SuiteRoot\tools\keygen -- gen $AppId)" }

$out = [IO.Path]::GetFullPath($out)
if ($out -ne (Join-Path ([IO.Path]::GetFullPath($PSScriptRoot)) 'dist-release')) { throw 'Invalid output directory' }
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory $pub -Force | Out-Null

Write-Host "==> publish $AppId $Version"
dotnet publish $Project -c Release -r $Rid --self-contained false -p:PublishSingleFile=true `
  -p:Version=$Version -p:AssemblyVersion="$($Version -replace '-.*$','').0" "-p:TSuiteRoot=$SuiteRoot" -o $pub -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish basarisiz' }

$artifact = Join-Path $out $ArtifactAs
if ($ArtifactAs.EndsWith('.zip')) {
  Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $artifact -Force
} else {
  Copy-Item (Join-Path $pub $ExeName) $artifact
}

$sha = (Get-FileHash $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "==> sha256 $sha"

$keygenProj = Join-Path $SuiteRoot 'tools\keygen\keygen.csproj'
$signArgs = @('sign', $AppId, $Version, $sha, $keyPem, '--channel', $Channel)
if ($MinVersion) { $signArgs += @('--min-version', $MinVersion) }
if ($BlockedVersions.Count) { $signArgs += @('--blocked-versions', ($BlockedVersions -join ',')) }
$signatureOutput = dotnet run --project $keygenProj -c Release -v q -- @signArgs
if ($LASTEXITCODE -ne 0) { throw 'imza uretilemedi' }
$sig = [string]($signatureOutput | Select-Object -Last 1)
$signatures = $sig.Trim() | ConvertFrom-Json
$sig = $signatures.signature
if (-not $sig) { throw 'imza uretilemedi' }

$url = "https://github.com/$repo/releases/download/v$Version/$ArtifactAs"
$latest = [ordered]@{
  version  = $Version
  notes    = $Notes
  pub_date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  url      = $url
  sha256   = $sha
  signature = $sig
  signature2 = $signatures.signature2
  channel   = $Channel
}
if ($MinVersion) { $latest.min_version = $MinVersion }
if ($BlockedVersions.Count) { $latest.blocked_versions = @($BlockedVersions) }
$latestPath = Join-Path $out 'latest-beta.json'
[IO.File]::WriteAllText($latestPath, ($latest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
$manifestPaths = @($latestPath)
if ($Channel -eq 'stable') {
  $stablePath = Join-Path $out 'latest.json'
  Copy-Item -LiteralPath $latestPath -Destination $stablePath
  $manifestPaths += $stablePath
}
Write-Host "==> $latestPath yazildi"

if ($Publish -and -not $NoPublish) {
  Write-Host "==> gh release create v$Version --repo $repo"
  $releaseArgs = @('release', 'create', "v$Version", $artifact) + $manifestPaths + @( '--repo', $repo, '--title', "$AppId v$Version")
  if ($Notes) { $releaseArgs += @('--notes', $Notes) } else { $releaseArgs += '--generate-notes' }
  if ($Channel -eq 'beta') { $releaseArgs += '--prerelease' }
  gh @releaseArgs
  if ($LASTEXITCODE -ne 0) { throw 'gh release create basarisiz' }
  if ($Channel -eq 'beta') {
    # /releases/latest excludes prereleases: put the beta pointer beside the stable manifest.
    $stableTag = gh api "repos/$repo/releases/latest" --jq '.tag_name'
    if ($LASTEXITCODE -ne 0) { throw 'Publish a stable baseline before beta releases' }
    gh release upload $stableTag $latestPath --repo $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Beta pointer upload failed' }
  }
} else {
  Write-Host "==> YAYINLANMADI (-Publish verilmedi). Dosyalar: $out"
}
