#!/usr/bin/env pwsh
# Publishes self-contained, single-file console executables for Windows and Linux.
# No .NET runtime needs to be installed on the target machine. Edit the appsettings.json that lands
# next to the binary (or set Control__* environment variables) to point at your SQL Server + game servers.
$ErrorActionPreference = 'Stop'

$proj = Join-Path $PSScriptRoot 'TControl.Server/TControl.Server.csproj'
$rids = @('win-x64', 'linux-x64')

foreach ($rid in $rids) {
    $out = Join-Path $PSScriptRoot "publish/$rid"
    Write-Host "==> Publishing $rid -> $out"
    dotnet publish $proj -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }
}

Write-Host "`nDone. Executables:"
Get-ChildItem (Join-Path $PSScriptRoot 'publish') -Recurse -Include 'TControl.Server', 'TControl.Server.exe' |
    ForEach-Object { "  $($_.FullName)" }
