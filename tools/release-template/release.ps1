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
  [Parameter(Mandatory = $true, Position = 1)][string]$Notes,
  [switch]$Publish,
  [switch]$NoPublish
)
$ErrorActionPreference = 'Stop'

# ===================== UYGULAMA AYARLARI =====================
$AppId      = 't-downloader'                                  # t-ramcash | t-desktree | ...
$Project    = 'src\TDownloader.App\TDownloader.App.csproj'    # publish edilecek proje
$ExeName    = 'TDownloader.App.exe'                            # publish ciktisindaki exe
$ArtifactAs = 'T-Downloader.exe'                               # surum dosyasi adi (.zip yazarsan klasor zip'lenir)
$Rid        = 'win-x64'
$SuiteRoot  = 'C:\Users\hp\Projects\t-suite'                   # keygen/sign araci icin
$GhOwner    = 'taha-sa'
# =============================================================

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Surum bicimi X.Y.Z olmali: $Version" }
$repo   = "$GhOwner/$AppId-releases"
$out    = Join-Path $PSScriptRoot 'dist-release'
$pub    = Join-Path $out 'pub'
$keyPem = Join-Path $env:USERPROFILE ".t-suite\keys\$AppId.pem"
if (-not (Test-Path $keyPem)) { throw "Ozel anahtar yok: $keyPem (once: dotnet run --project $SuiteRoot\tools\keygen -- gen $AppId)" }

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $pub -Force | Out-Null

Write-Host "==> publish $AppId $Version"
dotnet publish $Project -c Release -r $Rid --self-contained false -p:PublishSingleFile=true `
  -p:Version=$Version -p:AssemblyVersion="$($Version -replace '-.*$','').0" -o $pub
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
$sig = (dotnet run --project $keygenProj -c Release -- sign $AppId $Version $sha | Select-Object -Last 1).Trim()
if (-not $sig) { throw 'imza uretilemedi' }

$url = "https://github.com/$repo/releases/download/v$Version/$ArtifactAs"
$latest = [ordered]@{
  version  = $Version
  notes    = $Notes
  pub_date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  url      = $url
  sha256   = $sha
  signature = $sig
}
$latestPath = Join-Path $out 'latest.json'
[IO.File]::WriteAllText($latestPath, ($latest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Host "==> $latestPath yazildi"

if ($Publish -and -not $NoPublish) {
  Write-Host "==> gh release create v$Version --repo $repo"
  gh release create "v$Version" $artifact $latestPath --repo $repo --title "$AppId v$Version" --notes $Notes
  if ($LASTEXITCODE -ne 0) { throw 'gh release create basarisiz' }
} else {
  Write-Host "==> YAYINLANMADI (-Publish verilmedi). Dosyalar: $out"
}
