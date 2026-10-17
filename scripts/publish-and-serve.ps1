<#
.SYNOPSIS
    Publishes the client, checks the app shell has no external dependencies, and serves it locally.

.DESCRIPTION
    The caching service worker only exists in a published build, so this is how to test offline start-up:
      1. Run this script and open the URL it prints.
      2. Wait until DevTools -> Application -> Service workers shows the worker as "activated".
      3. DevTools -> Network -> Offline, then reload. The app must still start.

.PARAMETER Port
    Port to serve on. Default 5200.

.PARAMETER NoServe
    Publish and check only; don't start the server.

.EXAMPLE
    ./scripts/publish-and-serve.ps1
.EXAMPLE
    ./scripts/publish-and-serve.ps1 -Port 8080
#>
param(
    [int] $Port = 5200,
    [switch] $NoServe
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/publish'
$wwwroot = Join-Path $output 'wwwroot'

Write-Host 'Publishing the client (Release)...'
if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}
dotnet publish (Join-Path $repo 'src/OfflineCrud.Client') -c Release -o $output
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# The service worker can only cache this origin's files. A stylesheet, script or font loaded from another site
# (a CDN) would be missing offline, so fail the build if the app shell references one.
Write-Host 'Checking the app shell for external references...'
$pattern = '(?:src|href)\s*=\s*["'']https?://|url\(\s*["'']?https?://|@import\s+(?:url\()?["'']?https?://'
$hits = Get-ChildItem $wwwroot -Recurse -Include *.html, *.css | Select-String -Pattern $pattern
if ($hits) {
    $hits | ForEach-Object { Write-Host "  $($_.Path):$($_.LineNumber)" -ForegroundColor Red }
    Write-Error 'The app shell loads files from another origin; they will not be available offline.'
}

$assetsManifest = Get-Content (Join-Path $wwwroot 'service-worker-assets.js') -Raw
$assetCount = ([regex]::Matches($assetsManifest, '"url":')).Count
Write-Host "OK: no external references; the service worker manifest lists $assetCount files."

if ($NoServe) {
    exit 0
}

dotnet run (Join-Path $repo 'tools/serve.cs') -- $wwwroot $Port
