param([string]$Edge, [string]$Python = 'python', [string]$PillowPath)
$ErrorActionPreference = 'Stop'
if (-not $Edge) {
    $candidates = @("${env:ProgramFiles(x86)}/Microsoft/Edge/Application/msedge.exe", "$env:ProgramFiles/Microsoft/Edge/Application/msedge.exe", "$env:LOCALAPPDATA/Microsoft/Edge/Application/msedge.exe")
    $candidates += @(Get-ChildItem "${env:ProgramFiles(x86)}/Microsoft/EdgeCore/*/msedge.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName)
    $Edge = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Edge) { throw 'Microsoft Edge not found. Pass -Edge with msedge.exe path.' }
& $Python -I "$PSScriptRoot/prepare-icons.py" $PSScriptRoot $PillowPath
if ($LASTEXITCODE -ne 0) { throw 'Python 3 + Pillow required (no packages are installed by this script).' }
$manifest = Get-Content "$PSScriptRoot/out/render-jobs.json" -Raw | ConvertFrom-Json
& "$PSScriptRoot/edge-render.ps1" -Edge $Edge -Manifest "$PSScriptRoot/out/render-jobs.json"
& $Python -I "$PSScriptRoot/finish-icons.py" $PSScriptRoot $PillowPath
if ($LASTEXITCODE -ne 0) { throw 'Icon validation/ICO assembly failed.' }
Write-Host "Built and verified six icon sets ($($manifest.Count) Edge rasterizations)."
