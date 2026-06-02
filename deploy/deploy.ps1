<#
.SYNOPSIS
    Local publish of AuthServer + deploy of prebuilt binaries to the home server.

.DESCRIPTION
    Compilation runs LOCALLY (dotnet publish). Only the published binaries
    (./publish) plus Dockerfile and docker-compose.yml are copied to the server.
    The server has no SDK, no restore, no compilation - the runtime image just
    COPYs ./publish. This avoids slow/fragile server-side Docker builds.

    NOTE: ASCII-only on purpose. Windows PowerShell 5.1 reads BOM-less .ps1 as
    ANSI, so non-ASCII comments would corrupt parsing.

.EXAMPLE
    ./deploy/deploy.ps1
#>
[CmdletBinding()]
param(
    [string]$RemoteHost = 'homeserver',
    [string]$RemoteDir  = '/data/docker/alexwow'
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = Join-Path $RepoRoot 'src/AlexWoW.AuthServer/AlexWoW.AuthServer.csproj'
$PublishDir = Join-Path $RepoRoot 'publish'

Write-Host '==> 1/4 Publish (Release)...' -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Project -c Release -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host '==> 2/4 Preparing remote directory...' -ForegroundColor Cyan
# Wipe the dir fully (MySQL data lives in a named volume, not here).
ssh $RemoteHost "rm -rf $RemoteDir && mkdir -p $RemoteDir"
if ($LASTEXITCODE -ne 0) { throw 'Failed to prepare remote directory.' }

Write-Host '==> 3/4 Copying binaries and configs...' -ForegroundColor Cyan
# scp on Windows treats "D:\path" as host:path, so use relative paths
# from the repo root (no colon).
Push-Location $RepoRoot
try {
    scp -r publish Dockerfile docker-compose.yml "${RemoteHost}:${RemoteDir}/"
    if ($LASTEXITCODE -ne 0) { throw 'scp failed.' }
}
finally {
    Pop-Location
}

Write-Host '==> 4/4 Starting on server (build = COPY only)...' -ForegroundColor Cyan
ssh $RemoteHost "cd $RemoteDir && docker compose up -d --build"
if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed.' }

Write-Host 'Deploy complete.' -ForegroundColor Green
