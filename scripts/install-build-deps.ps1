<#
.SYNOPSIS
    Headless install of the toolchain needed to build the 4ever sources on
    a single MSVC toolset (v143 / VS 2022) target.

.DESCRIPTION
    Installs, unattended, Visual Studio 2022 Build Tools with the v143 C++
    compiler, ATL, MFC and the Windows 11 SDK (10.0.22621) that the projects
    will be pinned to.

    The DirectX SDK (June 2010) is NOT installed: it is vendored in the repo at
    'Lib\3rdParty\DirectX9 (June 2010)' and every project references it by
    relative path, so no machine-wide DX SDK / DXSDK_DIR is needed.

    Run this FIRST. Afterwards, retarget every project to v143 manually in
    Visual Studio (Solution -> Retarget solution), then build.

    Requires an elevated (Administrator) PowerShell session.

.EXAMPLE
    # From an elevated PowerShell prompt:
    .\scripts\install-build-deps.ps1
#>
[CmdletBinding()]
param(
    [string]$SdkComponent = "Microsoft.VisualStudio.Component.Windows11SDK.22621"
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "`n==> $Message" -ForegroundColor Cyan }

Write-Host "=== 4ever build-dependency installer ===" -ForegroundColor White

# ---------------------------------------------------------------------------
# 1) Prerequisites
# ---------------------------------------------------------------------------
Write-Step "Checking prerequisites (admin + winget)"

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must be run from an elevated (Administrator) PowerShell session."
}

$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $winget) {
    throw "winget not found. Install 'App Installer' from the Microsoft Store, then re-run."
}
Write-Host "    winget: $($winget.Source)"

# Component set for a v143-only build of the 4ever sources.
$components = @(
    "Microsoft.VisualStudio.Workload.VCTools",           # Desktop C++ build tools
    "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",  # v143 compiler (x86 + x64)
    "Microsoft.VisualStudio.Component.VC.ATLMFC",         # v143 ATL + MFC
    $SdkComponent                                         # Windows 11 SDK 10.0.22621
)
$addArgs = ($components | ForEach-Object { "--add $_" }) -join " "

# ---------------------------------------------------------------------------
# 2) Install VS 2022 Build Tools (idempotent: winget skips if already present)
# ---------------------------------------------------------------------------
Write-Step "Installing VS 2022 Build Tools"
try {
    $override = "--quiet --norestart --wait --includeRecommended $addArgs"
    Write-Host "    winget install Microsoft.VisualStudio.2022.BuildTools"
    Write-Host "    override: $override"
    # Pin --source winget: the package lives there, and this avoids the 'msstore'
    # source whose cert check can fail (winget error 0x8a15005e).
    & winget install --id Microsoft.VisualStudio.2022.BuildTools -e `
        --source winget `
        --accept-source-agreements --accept-package-agreements `
        --override $override
}
catch {
    # winget returns non-zero when the package is already installed; that is not
    # fatal here -- the explicit 'modify' step below guarantees the components.
    Write-Warning "    winget install returned an error (often 'already installed'); continuing."
}

# ---------------------------------------------------------------------------
# 3) Verify the required components are present.
#    Step 2's winget --override already installs them; this confirms that and
#    only falls back to 'vs_installer modify' if something is actually missing.
#    (Note: vs_installer.exe does NOT accept --wait -- that flag belongs to the
#    bootstrapper and passing it yields exit code 87. It is omitted below.)
# ---------------------------------------------------------------------------
Write-Step "Verifying v143 / ATL / MFC / SDK components"

$installerDir = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
$vsInstaller  = Join-Path $installerDir "vs_installer.exe"
$vswhere      = Join-Path $installerDir "vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found at $vswhere (VS Build Tools install failed?)" }

$installPath = & $vswhere -products Microsoft.VisualStudio.Product.BuildTools `
    -latest -property installationPath
if (-not $installPath) { throw "Could not resolve Build Tools installation path via vswhere." }
Write-Host "    installPath: $installPath"

function Get-InstalledComponentIds {
    (& $vswhere -products * -include packages -format json | ConvertFrom-Json) |
        ForEach-Object { $_.packages } | Select-Object -ExpandProperty id -Unique
}

$installed = Get-InstalledComponentIds
$missing   = $components | Where-Object { $installed -notcontains $_ }

if (-not $missing) {
    Write-Host "    All required components present."
}
else {
    Write-Warning ("    Missing: {0}" -f ($missing -join ', '))
    if (-not (Test-Path $vsInstaller)) { throw "vs_installer.exe not found at $vsInstaller" }
    $missingAdd = ($missing | ForEach-Object { "--add $_" }) -join " "
    # No --wait here (unsupported by vs_installer.exe); -Wait on Start-Process
    # waits for the launcher, then we re-verify below.
    $modifyArgs = "modify --installPath `"$installPath`" --quiet --norestart $missingAdd"
    Write-Host "    vs_installer $modifyArgs"
    $p = Start-Process -FilePath $vsInstaller -ArgumentList $modifyArgs -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {  # 3010 = success, reboot required
        throw "vs_installer modify failed with exit code $($p.ExitCode)."
    }
    $stillMissing = $components | Where-Object { (Get-InstalledComponentIds) -notcontains $_ }
    if ($stillMissing) {
        throw ("Components still missing after modify: {0}" -f ($stillMissing -join ', '))
    }
    Write-Host "    Components installed."
}

# ---------------------------------------------------------------------------
# 4) Locate the Dev Shell entry point for building later
# ---------------------------------------------------------------------------
Write-Step "Locating VS Dev Shell"

$devShell = Join-Path $installPath "Common7\Tools\Launch-VsDevShell.ps1"
if (Test-Path $devShell) {
    Write-Host "    VsDevShell: $devShell"
}
else {
    Write-Warning "    Launch-VsDevShell.ps1 not found (build tools may still be finishing)."
}

# ---------------------------------------------------------------------------
# NOTE: DirectX SDK (June 2010) is intentionally NOT installed here.
# It is vendored in the repo at 'Lib\3rdParty\DirectX9 (June 2010)' and every
# project references it by relative path (Include + Lib\x86), so no machine-wide
# DX SDK or DXSDK_DIR is required.
# ---------------------------------------------------------------------------
Write-Step "Checking vendored DirectX SDK"
$repoRoot   = Split-Path -Parent $PSScriptRoot
$dxVendored = Join-Path $repoRoot "Lib\3rdParty\DirectX9 (June 2010)\Include\d3dx9.h"
if (Test-Path $dxVendored) {
    Write-Host "    OK: using vendored copy in repo (Lib\3rdParty\DirectX9 (June 2010))."
}
else {
    Write-Warning "    Vendored DirectX SDK not found at expected path; the client build will fail without it."
}

# ---------------------------------------------------------------------------
# Done + next steps
# ---------------------------------------------------------------------------
Write-Host "`nInstall complete." -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor White
Write-Host "  1. Open a fresh elevated PowerShell, then start the VS Dev Shell:"
Write-Host "       & '$devShell'"
Write-Host "  2. Retarget every project to v143 MANUALLY in Visual Studio:"
Write-Host "       Right-click the solution -> 'Retarget solution' -> Windows SDK 10.0.22621.0, Toolset v143."
Write-Host "  3. Build (x86 / Release), in dependency order:"
Write-Host "       Lib\Own\*  ->  Servers\TServer.sln  ->  Servers\TLogSvr\4SLogServer.sln"
Write-Host "       ->  Client\TClient.sln  ->  Tools\Happy\Happy.sln"
