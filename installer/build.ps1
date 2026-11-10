<#
.SYNOPSIS
  T-Suite kurulum programlarini derler (Inno Setup 6). Cikti: installer\out\
.DESCRIPTION
  Uygulama publish klasorleri verilmezse, uygulama depolarini OKUYARAK gecici bir staging klasorune
  `dotnet publish -c Release -r win-x64 --self-contained false` calistirir (yalniz okur; dotnet yine de depodaki bin ve obj uretim klasorlerini gunceller).
  CI: build.ps1 -RamCashDir <dir> -DownloaderDir <dir> -DesktreeDir <dir> -BridgeSetup <exe> -Version 1.2.0
.PARAMETER Skip  Derlenmeyecek hedefler: ramcash,downloader,desktree,suite
#>
[CmdletBinding()]
param(
    [string]$RamCashDir, [string]$DownloaderDir, [string]$DesktreeDir,
    [string]$RamCashRepo   = 'C:\Users\hp\.gemini\antigravity\scratch\memreduct_gui',
    [string]$DownloaderRepo = 'C:\Users\hp\T-Downloader',
    [string]$DesktreeRepo  = 'C:\Users\hp\.gemini\antigravity\scratch\AmberTree',
    [string]$ExtensionDir,
    [string]$BridgeSetup = 'C:\Users\hp\Projects\kopru\dist-release\0.3.1\T-Bridge_0.3.1_x64-setup.exe',
    [string]$Version,
    [string]$ViewerUrl,
    [string]$StagingRoot = (Join-Path ([IO.Path]::GetTempPath()) 't-suite-staging'),
    [string]$Iscc,
    [string[]]$Skip = @()
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (-not $Iscc) {
    $cands = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    $Iscc = $cands | Select-Object -First 1
}
if (-not $Iscc) { throw 'ISCC.exe bulunamadi. winget install --id JRSoftware.InnoSetup -e' }

function Publish-App([string]$name, [string]$repo, [string]$csproj, [string]$dest) {
    if (-not (Test-Path $csproj)) { throw "csproj yok: $csproj" }
    Write-Host "publish $name -> $dest"
    New-Item -ItemType Directory -Force $dest | Out-Null
    $out = & dotnet publish $csproj -c Release -r win-x64 --self-contained false -o $dest "-p:UseAppHost=true" --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) { $out | Select-String 'error' | Select-Object -First 15 | ForEach-Object { Write-Host $_ }; throw "publish basarisiz: $name" }
}
function Get-Ver([string]$csproj) {
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if ($m) { $m.Matches[0].Groups[1].Value } else { '1.0.0' }
}

$stg = Join-Path $StagingRoot 'apps'
if (-not $RamCashDir -and $Skip -notcontains 'ramcash' ) {
    $csp = Join-Path $RamCashRepo 'MemReductGUI.csproj'; $RamCashDir = Join-Path $stg 't-ramcash'
    Publish-App 't-ramcash' $RamCashRepo $csp $RamCashDir
    if (-not $Version) { $Version = Get-Ver $csp }
}
if (-not $DownloaderDir -and $Skip -notcontains 'downloader') {
    $DownloaderDir = Join-Path $stg 't-downloader'
    Publish-App 't-downloader-app'  $DownloaderRepo (Join-Path $DownloaderRepo 'src\TDownloader.App\TDownloader.App.csproj') $DownloaderDir
    Publish-App 't-downloader-host' $DownloaderRepo (Join-Path $DownloaderRepo 'src\TDownloader.Host\TDownloader.Host.csproj') $DownloaderDir
    if (-not $ExtensionDir) { $ExtensionDir = Join-Path $DownloaderRepo 'extension' }
}
if (-not $DesktreeDir -and $Skip -notcontains 'desktree') {
    $csp = Join-Path $DesktreeRepo 'AmberTree.csproj'; $DesktreeDir = Join-Path $stg 't-desktree'
    Publish-App 't-desktree' $DesktreeRepo $csp $DesktreeDir
}
if (-not $Version) { $Version = '1.1.0' }

# Eklenti dosyalarini (testler/node_modules haric) indirici paketine kopyala
if ($DownloaderDir -and $ExtensionDir -and (Test-Path $ExtensionDir)) {
    $dst = Join-Path $DownloaderDir 'extension'
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    New-Item -ItemType Directory -Force $dst | Out-Null
    robocopy $ExtensionDir $dst /E /XD tests node_modules .git /XF package.json package-lock.json *.md /NFL /NDL /NJH /NJS /NP | Out-Null
}

New-Item -ItemType Directory -Force (Join-Path $here 'out') | Out-Null
$common = @("/DAppVersion=$Version", '/Q')
if ($ViewerUrl) { $common += "/DViewerUrl=$ViewerUrl" }
$jobs = @(
    @{ n='ramcash';    f='t-ramcash.iss';    d=$RamCashDir;    p='SourceDir' },
    @{ n='downloader'; f='t-downloader.iss'; d=$DownloaderDir; p='SourceDir' },
    @{ n='desktree';   f='t-desktree.iss';   d=$DesktreeDir;   p='SourceDir' }
)
foreach ($j in $jobs) {
    if ($Skip -contains $j.n -or -not $j.d) { continue }
    Write-Host "ISCC $($j.f)"
    & $Iscc @common "/D$($j.p)=$($j.d)" (Join-Path $here $j.f)
    if ($LASTEXITCODE -ne 0) { throw "ISCC basarisiz: $($j.f)" }
}
if ($Skip -notcontains 'suite') {
    Write-Host 'ISCC t-suite-setup.iss'
    $a = @($common) + @("/DRamCashDir=$RamCashDir", "/DDownloaderDir=$DownloaderDir", "/DDeskTreeDir=$DesktreeDir")
    if ($BridgeSetup -and (Test-Path $BridgeSetup)) { $a += "/DBridgeSetup=$BridgeSetup" } else { Write-Warning "T-Bridge setup yok, suite onsuz derlenir: $BridgeSetup" ; $a += '/DBridgeSetup=__yok__' }
    & $Iscc @a (Join-Path $here 't-suite-setup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'ISCC basarisiz: t-suite-setup.iss' }
}
Get-ChildItem (Join-Path $here 'out') -Filter *.exe | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length/1MB) }
