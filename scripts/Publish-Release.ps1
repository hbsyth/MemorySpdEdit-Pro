#Requires -Version 5.1
param(
    [switch]$NoBump,
    [switch]$SkipPush,
    [switch]$SkipRelease,
    [string]$Repo = "hbsyth/MemorySpdEdit-Pro",
    [string]$Remote = "pro"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-IsoYearWeek([datetime]$date) {
    $cal = [System.Globalization.CultureInfo]::InvariantCulture.Calendar
    $week = $cal.GetWeekOfYear($date, [System.Globalization.CalendarWeekRule]::FirstFourDayWeek, [DayOfWeek]::Monday)
    $day = [int]$date.DayOfWeek
    if ($day -eq 0) { $day = 7 }
    $thu = $date.AddDays(4 - $day)
    return @{ Year = $thu.Year; Week = $week }
}

function Read-VersionProps {
    $path = Join-Path $root "Version.props"
    if (-not (Test-Path $path)) { throw "Version.props not found: $path" }
    $text = Get-Content -Raw -Path $path
    if ($text -notmatch '<AppVersionYear>(\d+)</AppVersionYear>') { throw "AppVersionYear missing" }
    $year = [int]$Matches[1]
    if ($text -notmatch '<AppVersionWeek>(\d+)</AppVersionWeek>') { throw "AppVersionWeek missing" }
    $week = [int]$Matches[1]
    if ($text -notmatch '<AppVersionSerial>(\d+)</AppVersionSerial>') { throw "AppVersionSerial missing" }
    $serial = [int]$Matches[1]
    return @{ Year = $year; Week = $week; Serial = $serial; Path = $path }
}

function Write-VersionProps([int]$year2, [int]$week, [int]$serial) {
    $label = "Ver{0:D2}.{1:D2}.{2:D4}" -f $year2, $week, $serial
    $asm = "{0}.{1}.{2}" -f $year2, $week, $serial
    $weekStr = "{0:D2}" -f $week
    $lines = @(
        '<!--',
        '  Version rule: Ver + YY + . + ISO-week(WW) + . + serial(NNNN)',
        '  Example: Ver26.38.0001',
        '  Bump only when publishing package to GitHub (scripts/Publish-Release.ps1).',
        ("  Current: {0}" -f $label),
        '-->',
        '<Project>',
        '  <PropertyGroup>',
        ("    <AppVersionYear>{0}</AppVersionYear>" -f $year2),
        ("    <AppVersionWeek>{0}</AppVersionWeek>" -f $weekStr),
        ("    <AppVersionSerial>{0}</AppVersionSerial>" -f $serial),
        ("    <AppVersionLabel>{0}</AppVersionLabel>" -f $label),
        ("    <Version>{0}</Version>" -f $asm),
        ("    <AssemblyVersion>{0}.0</AssemblyVersion>" -f $asm),
        ("    <FileVersion>{0}.0</FileVersion>" -f $asm),
        ("    <InformationalVersion>{0}</InformationalVersion>" -f $label),
        '    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>',
        '  </PropertyGroup>',
        '</Project>',
        ''
    )
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $root "Version.props"), ($lines -join "`n"), $utf8NoBom)
    return $label
}

$iso = Get-IsoYearWeek (Get-Date)
$year2 = $iso.Year % 100
$week = $iso.Week
$cur = Read-VersionProps

if ($NoBump) {
    $year2 = $cur.Year
    $week = $cur.Week
    $serial = $cur.Serial
    $label = "Ver{0:D2}.{1:D2}.{2:D4}" -f $year2, $week, $serial
    Write-Host ("==> Use current version {0} (no bump)" -f $label)
} else {
    if ($cur.Year -eq $year2 -and $cur.Week -eq $week) {
        $serial = $cur.Serial + 1
    } else {
        $serial = 1
    }
    if ($serial -gt 9999) { throw "serial exceeds 9999" }
    $label = Write-VersionProps $year2 $week $serial
    Write-Host ("==> Version bumped to {0}" -f $label)
}

Write-Host "==> Build framework-dependent single-file (no runtime bundled)"
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "launcher\publish-minimal.ps1")
if ($LASTEXITCODE -ne 0) { throw "publish-minimal failed" }

$exeName = "MemorySpdEdit-Pro-$label.exe"
$exe = Join-Path $root "publish\minimal\$exeName"
if (-not (Test-Path $exe)) { throw ("missing {0}" -f $exe) }

$zipName = "MemorySpdEdit-Pro-$label.zip"
$zipPath = Join-Path $root $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $exe -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host ("==> Packed {0}" -f $zipName)

if (-not $env:GIT_AUTHOR_NAME) { $env:GIT_AUTHOR_NAME = "hbsyth" }
if (-not $env:GIT_AUTHOR_EMAIL) { $env:GIT_AUTHOR_EMAIL = "hbsyth@qq.com" }
$env:GIT_COMMITTER_NAME = $env:GIT_AUTHOR_NAME
$env:GIT_COMMITTER_EMAIL = $env:GIT_AUTHOR_EMAIL

git add -- Version.props SpdEditor.csproj AppVersion.cs AppPaths.cs MainForm.cs MainForm.Designer.cs `
    Program.cs XmpInfoForm.cs README.md scripts/Publish-Release.ps1 launcher/publish-minimal.ps1
git add -- Core/ .cursor/rules/
git add -u -- .
$pending = git status --porcelain
if ($pending) {
    $msg = "Release {0} - bump version and sync packaging sources." -f $label
    git commit -m $msg
    if (-not $SkipPush) {
        git push $Remote HEAD
        Write-Host ("==> Pushed source to {0}" -f $Remote)
    }
} else {
    Write-Host "==> No source changes to commit"
}

if (-not $SkipRelease) {
    $gh = "C:\Program Files\GitHub CLI\gh.exe"
    if (-not (Test-Path $gh)) { $gh = "gh" }
    $title = "MemorySpdEdit Pro {0} Release" -f $label
    $notesPath = Join-Path $env:TEMP ("mse-release-notes-{0}.md" -f $label)
    $notes = @(
        ("## MemorySpdEdit Pro {0}" -f $label),
        "",
        "Framework-dependent single-file package (requires .NET 10 Desktop Runtime x64).",
        "The zip contains one exe only — runtime dependencies are NOT bundled.",
        "",
        ("Version rule: Ver + YY + . + ISO-week(WW) + . + serial(NNNN). This build: {0}." -f $label),
        "",
        "### Asset",
        ('- `{0}` - extract and run `{1}`' -f $zipName, $exeName)
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText($notesPath, $notes, (New-Object System.Text.UTF8Encoding $true))
    & $gh release create $label $zipPath --repo $Repo --title $title --notes-file $notesPath
    Write-Host ("==> https://github.com/{0}/releases/tag/{1}" -f $Repo, $label)
}

Write-Host ("Done: {0}" -f $label)
