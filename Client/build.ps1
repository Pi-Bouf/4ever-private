# Builds the client with CMake + Ninja inside an x86 Visual Studio developer shell.
#   .\Client\build.ps1                  # Release build into build\msvc-x86\bin\TClient.exe
#   .\Client\build.ps1 -Preset msvc-x86 -Deploy   # also copies TClient.exe into Game\
#   .\Client\build.ps1 -Preset clang-x86          # clang-cl build into build\clang-x86\bin\
param(
    [string]$Preset = "msvc-x86",
    [switch]$Deploy,
    [switch]$Clean
)
$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio with the C++ x86/x64 tools was not found." }

# Enter the x86 developer environment (cl, rc, link, INCLUDE/LIB with ATL/MFC, cmake, ninja).
# VsDevCmd itself calls vswhere, so it must be on PATH.
$env:PATH = "$(Split-Path $vswhere);$env:PATH"
Import-Module "$vs\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments "-arch=x86 -host_arch=x64" | Out-Null
# clang-cl / lld-link for the clang-x86 preset (64-bit host tools; the preset targets i686).
$llvm = "$vs\VC\Tools\Llvm\x64\bin"
if (Test-Path $llvm) { $env:PATH = "$llvm;$env:PATH" }

Push-Location $PSScriptRoot
try {
    $binaryDir = Join-Path $PSScriptRoot "..\build\$Preset"
    if ($Clean -and (Test-Path $binaryDir)) { Remove-Item -Recurse -Force $binaryDir }

    $deployFlag = if ($Deploy) { "ON" } else { "OFF" }
    cmake --preset $Preset "-DTCLIENT_DEPLOY_TO_GAME=$deployFlag"
    if ($LASTEXITCODE) { throw "CMake configure failed." }
    cmake --build --preset $Preset
    if ($LASTEXITCODE) { throw "Build failed." }
} finally {
    Pop-Location
}
