# Publish framework-dependent single-file app, then wrap with native .NET 10 guard launcher.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Resolve-Path "$PSScriptRoot\..").Path }
Set-Location $root

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -property installationPath
if (-not $vs) { throw "Visual Studio not found (need MSVC for launcher)" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found" }

$stage = Join-Path $root "publish\_minimal_stage"
$outDir = Join-Path $root "publish\minimal"
New-Item -ItemType Directory -Force -Path $stage, $outDir, (Join-Path $root "launcher\build") | Out-Null

Write-Host "==> Publish framework-dependent single-file payload"
dotnet publish SpdEditor.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $stage
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$payloadSrc = Join-Path $stage "MemorySpdEdit-Pro.exe"
if (-not (Test-Path $payloadSrc)) { throw "Payload missing: $payloadSrc" }
Copy-Item $payloadSrc (Join-Path $root "launcher\payload.exe") -Force

Write-Host "==> Build native launcher (checks .NET 10 Desktop Runtime)"
$buildCmd = @"
call "$vcvars" >nul && cd /d "$root\launcher" && rc /nologo /fo build\launcher.res launcher.rc && cl /nologo /O2 /W3 /utf-8 /DUNICODE /D_UNICODE /Fe:build\MemorySpdEdit-Pro.exe main.cpp build\launcher.res /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib ole32.lib
"@
cmd.exe /c $buildCmd
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed" }

Copy-Item (Join-Path $root "launcher\build\MemorySpdEdit-Pro.exe") (Join-Path $outDir "MemorySpdEdit-Pro.exe") -Force
Remove-Item (Join-Path $root "launcher\payload.exe") -Force -ErrorAction SilentlyContinue

$size = (Get-Item (Join-Path $outDir "MemorySpdEdit-Pro.exe")).Length
Write-Host ("OK: publish\minimal\MemorySpdEdit-Pro.exe ({0:N2} MB)" -f ($size / 1MB))
Write-Host "Missing .NET 10 -> Chinese prompt + download Desktop Runtime x64"
