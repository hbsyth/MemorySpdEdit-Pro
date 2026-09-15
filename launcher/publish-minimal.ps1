# Publish self-contained single-file (one exe, no .NET runtime install required).
# Output: publish\minimal\MemorySpdEdit-Pro-VerYY.WW.NNNN.exe
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Resolve-Path "$PSScriptRoot\..").Path }
Set-Location $root

function Get-AppVersionLabel {
    $path = Join-Path $root "Version.props"
    if (-not (Test-Path $path)) { throw "Version.props not found: $path" }
    $text = Get-Content -Raw -Path $path
    if ($text -notmatch '<AppVersionLabel>([^<]+)</AppVersionLabel>') {
        throw "AppVersionLabel missing in Version.props"
    }
    return $Matches[1].Trim()
}

$label = Get-AppVersionLabel
$outExeName = "MemorySpdEdit-Pro-$label.exe"

$stage = Join-Path $root "publish\_minimal_stage"
$outDir = Join-Path $root "publish\minimal"
New-Item -ItemType Directory -Force -Path $stage, $outDir | Out-Null

Write-Host ("==> Version {0}" -f $label)
Write-Host "==> Publish self-contained single-file"
dotnet publish SpdEditor.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=false -p:PublishReadyToRun=false -p:PublishTrimmed=false `
  -o $stage
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$payloadSrc = Join-Path $stage "MemorySpdEdit-Pro.exe"
if (-not (Test-Path $payloadSrc)) { throw "Publish output missing: $payloadSrc" }

Get-ChildItem -Path $outDir -Filter "MemorySpdEdit-Pro*.exe" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$outPath = Join-Path $outDir $outExeName
Copy-Item $payloadSrc $outPath -Force

$size = (Get-Item $outPath).Length
Write-Host ("OK: publish\minimal\{0} ({1:N2} MB)" -f $outExeName, ($size / 1MB))
Write-Host "Self-contained win-x64 — no .NET runtime install required."
