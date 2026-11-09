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
$signatureOutput = dotnet run --project $keygenProj -c Release -v q -- sign $AppId $Version $sha $keyPem
if ($LASTEXITCODE -ne 0) { throw 'imza uretilemedi' }
$sig = [string]($signatureOutput | Select-Object -Last 1)
$sig = $sig.Trim()
if (-not $sig) { throw 'imza uretilemedi' }

$url = "https://github.com/$repo/releases/download/v$Version/$ArtifactAs"
$latest = [ordered]@{
  version  = $Version
  notes    = $Notes
  pub_date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  url      = $url
  sha256   = $sha
  signature = $sig
  channel   = $Channel
}
$latestPath = Join-Path $out 'latest.json'
[IO.File]::WriteAllText($latestPath, ($latest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Host "==> $latestPath yazildi"

if ($Publish -and -not $NoPublish) {
  Write-Host "==> gh release create v$Version --repo $repo"
  $releaseArgs = @('release', 'create', "v$Version", $artifact, $latestPath, '--repo', $repo, '--title', "$AppId v$Version")
  if ($Notes) { $releaseArgs += @('--notes', $Notes) } else { $releaseArgs += '--generate-notes' }
  if ($Channel -eq 'beta') { $releaseArgs += '--prerelease' }
  gh @releaseArgs
  if ($LASTEXITCODE -ne 0) { throw 'gh release create basarisiz' }
} else {
  Write-Host "==> YAYINLANMADI (-Publish verilmedi). Dosyalar: $out"
}
