<#
.SYNOPSIS
    Build the whole 4ever solution set (Release|x86) in dependency order.

.DESCRIPTION
    Locates MSBuild from the installed VS 2022 Build Tools (via vswhere) and
    builds each solution in an order that satisfies cross-solution .lib
    dependencies:

        1. Lib\Own\TachyonControl        (standalone UI-control lib)
        2. Servers\TServer.sln           (servers + TNetLib + TProtocol)
        3. Servers\TLogSvr\4SLogServer   (UDP log sink; separate solution)
        4. Client\TClient.sln            (client + Engine Lib, TChart, TCML, TComp, HwidLib)
        5. Tools\TCMLParser              (consumes TComp / TCML / Engine Lib)
        6. Tools\TLauncher (4Story)      (consumes Engine Lib)
        7. Tools\Happy                   (consumes TProtocol / TNetLib)

    By default it keeps going after a failure and prints a pass/fail summary at
    the end (so you see every broken solution in one run). Use -StopOnError to
    halt at the first failure. Exit code is non-zero if any solution failed.

    NOT built here (no solution wraps them): Lib\Own\TServerSystem (fork addition,
    not referenced by any .sln). The per-lib solutions under Lib\Own\* are skipped
    on purpose -- TClient.sln / TServer.sln already build those projects.

.PARAMETER Configuration
    MSBuild configuration. Default: Release.

.PARAMETER Platform
    MSBuild platform. Default: x86 (every solution exposes Release|x86).

.PARAMETER Clean
    Use /t:Rebuild (clean + build) instead of /t:Build.

.PARAMETER StopOnError
    Stop at the first solution that fails.

.PARAMETER LogDir
    Where to write per-solution MSBuild logs. Default: a build-logs folder next
    to this script.

.EXAMPLE
    .\scripts\build-all.ps1
    .\scripts\build-all.ps1 -Clean
    .\scripts\build-all.ps1 -Configuration Debug -StopOnError
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform      = "x86",
    [switch]$Clean,
    [switch]$StopOnError,
    [string]$LogDir
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$m) Write-Host "`n==> $m" -ForegroundColor Cyan }

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $LogDir) { $LogDir = Join-Path $PSScriptRoot "build-logs" }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

# --- Locate MSBuild from the installed VS 2022 Build Tools -------------------
Write-Step "Locating MSBuild"
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found -- is VS Build Tools installed? Run scripts\install-build-deps.ps1 first." }

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild -or -not (Test-Path $msbuild)) { throw "Could not locate MSBuild.exe via vswhere." }
Write-Host "    $msbuild"

$target = if ($Clean) { "Rebuild" } else { "Build" }

# --- Solutions, in build order ----------------------------------------------
$solutions = @(
    "Lib\Own\TachyonControl\TachyonControl.sln",
    "Servers\TServer.sln",
    "Servers\TLogSvr\4SLogServer.sln",
    "Client\TClient.sln",
    "Tools\TCMLParser\TCMLParser.sln",
    "Tools\TLauncher\4Story.sln",
    "Tools\Happy\Happy.sln"
)

Write-Host ("`nConfiguration : {0}|{1}   Target: {2}" -f $Configuration, $Platform, $target)
Write-Host ("Logs          : {0}" -f $LogDir)

$results = @()
foreach ($rel in $solutions) {
    $full = Join-Path $repoRoot $rel
    $name = [System.IO.Path]::GetFileNameWithoutExtension($rel)
    Write-Step ("Building {0}" -f $rel)

    if (-not (Test-Path $full)) {
        Write-Warning "    Solution not found: $full"
        $results += [pscustomobject]@{ Solution = $rel; Result = 'MISSING'; Exit = -1 }
        if ($StopOnError) { break } else { continue }
    }

    $log = Join-Path $LogDir ("{0}.log" -f ($name -replace '[^\w.-]', '_'))
    $args = @(
        $full,
        "/t:$target",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/m",
        "/nologo",
        "/v:minimal",
        "/clp:Summary",
        "/flp:LogFile=$log;Verbosity=normal"
    )
    & $msbuild @args
    $code = $LASTEXITCODE

    if ($code -eq 0) {
        Write-Host ("    OK  ({0})" -f $rel) -ForegroundColor Green
        $results += [pscustomobject]@{ Solution = $rel; Result = 'OK'; Exit = 0 }
    }
    else {
        Write-Host ("    FAILED (exit {0}) -- see {1}" -f $code, $log) -ForegroundColor Red
        $results += [pscustomobject]@{ Solution = $rel; Result = 'FAILED'; Exit = $code }
        if ($StopOnError) { break }
    }
}

# --- Summary -----------------------------------------------------------------
Write-Step "Build summary"
$results | Format-Table -AutoSize | Out-String | Write-Host

$failed = @($results | Where-Object { $_.Result -ne 'OK' })
if ($failed.Count -gt 0) {
    Write-Host ("{0} solution(s) did not build. Logs in {1}" -f $failed.Count, $LogDir) -ForegroundColor Red
    exit 1
}
else {
    Write-Host "All solutions built successfully." -ForegroundColor Green
    exit 0
}
