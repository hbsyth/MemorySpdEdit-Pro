#Requires -Version 5.1
<#
.SYNOPSIS
  按规则递增版本号，编译无依赖单一文件，打包 ZIP，提交推送源码，发布 GitHub Release。

.DESCRIPTION
  版本格式：Ver + 年度后2位 + . + ISO周度(2位) + . + 流水号(4位)
  例：Ver26.38.0001
  - 同一年周：流水号 +1
  - 跨周：流水号从 0001 起
  仅本脚本在「向 GitHub 发布打包」时变更 Version.props，并同步仓库。

.PARAMETER NoBump
  不递增，使用 Version.props 当前版本发布。

.PARAMETER SkipPush / SkipRelease
  跳过推送或创建 Release（本地试跑）。
#>
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
    if ($day -eq 0) { $day = 7 } # Sunday=7
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
    $content = @"
<!--
  产品版本规则：Ver + 年度后2位 + . + 周度(2位) + . + 流水号(4位)
  例：Ver26.38.0001
  仅在向 GitHub 推送发布打包程序时变更（scripts/Publish-Release.ps1）；
  变更后写入本文件并同步 GitHub 源码。
  当前：$label
-->
<Project>
  <PropertyGroup>
    <AppVersionYear>$year2</AppVersionYear>
    <AppVersionWeek>$weekStr</AppVersionWeek>
    <AppVersionSerial>$serial</AppVersionSerial>
    <AppVersionLabel>$label</AppVersionLabel>
    <Version>$asm</Version>
    <AssemblyVersion>$asm.0</AssemblyVersion>
    <FileVersion>$asm.0</FileVersion>
    <InformationalVersion>$label</InformationalVersion>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
  </PropertyGroup>
</Project>
"@
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $root "Version.props"), $content, $utf8NoBom)
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
    Write-Host "==> 使用当前版本 $label（未递增）"
} else {
    if ($cur.Year -eq $year2 -and $cur.Week -eq $week) {
        $serial = $cur.Serial + 1
    } else {
        $serial = 1
    }
    if ($serial -gt 9999) { throw "流水号超过 9999" }
    $label = Write-VersionProps $year2 $week $serial
    Write-Host "==> 版本号变更为 $label"
}

Write-Host "==> 编译无依赖单一文件"
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "launcher\publish-minimal.ps1")
if ($LASTEXITCODE -ne 0) { throw "publish-minimal failed" }

$exe = Join-Path $root "publish\minimal\MemorySpdEdit-Pro.exe"
if (-not (Test-Path $exe)) { throw "missing $exe" }

$zipName = "MemorySpdEdit-Pro-$label.zip"
$zipPath = Join-Path $root $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $exe -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "==> 已打包 $zipName"

$env:GIT_AUTHOR_NAME = if ($env:GIT_AUTHOR_NAME) { $env:GIT_AUTHOR_NAME } else { "hbsyth" }
$env:GIT_AUTHOR_EMAIL = if ($env:GIT_AUTHOR_EMAIL) { $env:GIT_AUTHOR_EMAIL } else { "hbsyth@qq.com" }
$env:GIT_COMMITTER_NAME = $env:GIT_AUTHOR_NAME
$env:GIT_COMMITTER_EMAIL = $env:GIT_AUTHOR_EMAIL

git add -A -- Version.props SpdEditor.csproj AppVersion.cs MainForm.Designer.cs `
    installer/SetupApp/SetupApp.csproj installer/SetupApp/Program.cs `
    README.md scripts/Publish-Release.ps1
git add -u -- .
$pending = git status --porcelain
if ($pending) {
    git commit -m "Release $label: bump version and sync packaging sources."
    if (-not $SkipPush) {
        git push $Remote HEAD
        Write-Host "==> 已推送源码到 $Remote"
    }
} else {
    Write-Host "==> 无源码变更可提交"
}

if (-not $SkipRelease) {
    $gh = "C:\Program Files\GitHub CLI\gh.exe"
    if (-not (Test-Path $gh)) { $gh = "gh" }
    $notes = @"
## MemorySpdEdit Pro $label

无依赖包单一文件发布包（需本机安装 .NET 10 Desktop Runtime x64；缺失时启动器会提示下载）。

版本规则：``Ver`` + 年(2) + ``.`` + ISO周(2) + ``.`` + 流水(4)。本包对应 ``$label``。

### 资源
- ``$zipName`` — 解压后运行 ``MemorySpdEdit-Pro.exe``
"@
    & $gh release create $label $zipPath --repo $Repo --title "MemorySpdEdit Pro $label 发布版" --notes $notes
    Write-Host "==> https://github.com/$Repo/releases/tag/$label"
}

Write-Host "完成：$label"
