#!/usr/bin/env pwsh
# Publishes self-contained, single-file executables for Windows and Linux.
$ErrorActionPreference = 'Stop'

$proj = Join-Path $PSScriptRoot 'TPatch.Server/TPatch.Server.csproj'
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
Get-ChildItem (Join-Path $PSScriptRoot 'publish') -Recurse -Include 'TPatch.Server', 'TPatch.Server.exe' |
    ForEach-Object { "  $($_.FullName)" }
