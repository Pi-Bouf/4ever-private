<#
.SYNOPSIS
    Build the 4ever game client and the launcher (Release|x86).

.DESCRIPTION
    Builds, in order:
        1. Client\TClient.sln       -- TClient + its libs (Engine Lib, TChart,
                                        TCML, TComp, HwidLib) all build from this
                                        one solution.
        2. Tools\TLauncher\4Story.sln -- the launcher; links Engine Lib.lib, so it
                                        must build AFTER TClient.

    Locates MSBuild from the installed VS 2022 Build Tools via vswhere. Writes a
    per-solution log and prints a pass/fail summary. Exit code is non-zero if any
    solution failed.

.PARAMETER Configuration   MSBuild configuration. Default: Release.
.PARAMETER Platform        MSBuild platform. Default: x86.
.PARAMETER Clean           Use /t:Rebuild (clean + build) instead of /t:Build.
.PARAMETER StopOnError     Stop at the first solution that fails.
.PARAMETER LogDir          Where to write logs. Default: build-logs next to this script.

.EXAMPLE
    .\scripts\build-client.ps1
    .\scripts\build-client.ps1 -Clean
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

# --- Locate MSBuild ---------------------------------------------------------
Write-Step "Locating MSBuild"
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found -- run scripts\install-build-deps.ps1 first." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild -or -not (Test-Path $msbuild)) { throw "Could not locate MSBuild.exe via vswhere." }
Write-Host "    $msbuild"

$target = if ($Clean) { "Rebuild" } else { "Build" }

# --- Solutions, in build order ----------------------------------------------
$solutions = @(
    "Client\TClient.sln",            # client + Engine Lib, TChart, TCML, TComp, HwidLib
    "Tools\TLauncher\4Story.sln"     # launcher; links Engine Lib.lib -> after TClient
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

    $log  = Join-Path $LogDir ("{0}.log" -f ($name -replace '[^\w.-]', '_'))
    $args = @(
        $full, "/t:$target",
        "/p:Configuration=$Configuration", "/p:Platform=$Platform",
        "/m", "/nologo", "/v:minimal", "/clp:Summary",
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

# --- Summary ----------------------------------------------------------------
Write-Step "Build summary"
$results | Format-Table -AutoSize | Out-String | Write-Host

$failed = @($results | Where-Object { $_.Result -ne 'OK' })
if ($failed.Count -gt 0) {
    Write-Host ("{0} solution(s) did not build. Logs in {1}" -f $failed.Count, $LogDir) -ForegroundColor Red
    exit 1
}
Write-Host "All solutions built successfully." -ForegroundColor Green
exit 0
