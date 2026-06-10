<#
.SYNOPSIS
    Local publish of AuthServer + WorldServer and deploy of prebuilt binaries.

.DESCRIPTION
    Compilation runs LOCALLY (dotnet publish). Only the published binaries
    (./publish/auth, ./publish/world) plus the Dockerfiles and docker-compose.yml
    are copied to the server. The server has no SDK, no restore, no compilation -
    the runtime images just COPY the published output. This avoids slow/fragile
    server-side Docker builds.

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
$AuthProj   = Join-Path $RepoRoot 'src/AlexWoW.AuthServer/AlexWoW.AuthServer.csproj'
$WorldProj  = Join-Path $RepoRoot 'src/AlexWoW.WorldServer/AlexWoW.WorldServer.csproj'
$WebProj    = Join-Path $RepoRoot 'src/AlexWoW.Web/AlexWoW.Web.csproj'
$PublishDir = Join-Path $RepoRoot 'publish'

Write-Host '==> 1/4 Publish AuthServer + WorldServer + Web (Release)...' -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $AuthProj  -c Release -o (Join-Path $PublishDir 'auth')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (auth) failed.' }
dotnet publish $WorldProj -c Release -o (Join-Path $PublishDir 'world')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (world) failed.' }
dotnet publish $WebProj   -c Release -o (Join-Path $PublishDir 'web')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (web) failed.' }

Write-Host '==> 2/4 Preparing remote directory...' -ForegroundColor Cyan
# Wipe the dir fully (MySQL data lives in a named volume, not here).
ssh $RemoteHost "rm -rf $RemoteDir && mkdir -p $RemoteDir"
if ($LASTEXITCODE -ne 0) { throw 'Failed to prepare remote directory.' }

Write-Host '==> 3/4 Copying binaries and configs...' -ForegroundColor Cyan
# scp on Windows treats "D:\path" as host:path, so use relative paths
# from the repo root (no colon).
Push-Location $RepoRoot
try {
    scp -r publish Dockerfile.auth Dockerfile.world Dockerfile.web docker-compose.yml "${RemoteHost}:${RemoteDir}/"
    if ($LASTEXITCODE -ne 0) { throw 'scp failed.' }
}
finally {
    Pop-Location
}

Write-Host '==> 4/4 Starting on server (build = COPY only)...' -ForegroundColor Cyan
# Merge docker's stderr (build progress) into stdout ON THE REMOTE side. Windows PowerShell 5.1
# with ErrorActionPreference=Stop otherwise treats that stderr as a terminating NativeCommandError
# even when docker exits 0.
ssh $RemoteHost "cd $RemoteDir && docker compose up -d --build 2>&1"
if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed.' }

Write-Host 'Deploy complete.' -ForegroundColor Green
