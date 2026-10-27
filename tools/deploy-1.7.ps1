# Trader Ships - deploy 1.7 build to the RimWorld Mods folder.
# Pure-ASCII on purpose: Windows PowerShell 5.1 misreads UTF-8 files without a BOM.
#
# IMPORTANT: this script rebuilds $Target from scratch each run. Copy-Item -Recurse into an
# existing directory nests the source folder inside it (producing 1.7/1.7/), so we always
# start from a clean destination.
param(
    [Parameter(Mandatory = $false)]
    [string]$Source = 'E:\AI\mods\TraderShips',
    [Parameter(Mandatory = $false)]
    [string]$Target = 'H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Source)) { throw "Source dir not found: $Source" }
if (-not (Test-Path "$Source\1.7\Assemblies\TraderShips.dll")) {
    throw "Missing build output: $Source\1.7\Assemblies\TraderShips.dll (build first)"
}

# Clean destination so no stale/nested folders survive
if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Target | Out-Null

function Copy-Tree([string]$From, [string]$To) {
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    Copy-Item (Join-Path $From '*') $To -Recurse -Force
}

# About (1.7 metadata)
Copy-Tree (Join-Path $Source 'About') (Join-Path $Target 'About')
Copy-Item (Join-Path $Source 'About\About-1.7.xml') (Join-Path $Target 'About\About.xml') -Force

# Do NOT ship a PublishedFileId.txt unless the source has one. A stale/original ID here makes
# ModUpdater overwrite someone else's workshop entry instead of creating our own.
if (-not (Test-Path (Join-Path $Source 'About\PublishedFileId.txt'))) {
    Remove-Item (Join-Path $Target 'About\PublishedFileId.txt') -Force -ErrorAction SilentlyContinue
}

# Versioned content: 1.7 -> <target>\1.7  (never nested)
$verSrc = Join-Path $Source '1.7'
if (Test-Path $verSrc) {
    Copy-Tree $verSrc (Join-Path $Target '1.7')
    # Only the compiled dll belongs in Assemblies
    Get-ChildItem (Join-Path $Target '1.7\Assemblies') -Filter '*.deps.json' -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem (Join-Path $Target '1.7\Assemblies') -Filter '*.csproj*' -ErrorAction SilentlyContinue | Remove-Item -Force
}

# Version-independent shared assets (copied flat, no nesting)
foreach ($shared in @('Textures', 'Sounds', 'Languages', 'Patches')) {
    $src = Join-Path $Source $shared
    if (Test-Path $src) {
        Copy-Tree $src (Join-Path $Target $shared)
    }
}

# LoadFolders: <v1.7> is the canonical key. RimWorld's InitLoadFolders first tries an exact
# match against the full version string (e.g. 1.7.4990); failing that it falls back to the
# newest key that is <= the current version. "1.7" parses to Version(1,7,0), which satisfies
# that fallback, so this single key is sufficient and is what vanilla mods use.
$loadFolders = @'
<?xml version="1.0" encoding="utf-8"?>
<loadFolders>
  <v1.7>
    <li>/</li>
    <li>1.7</li>
  </v1.7>
</loadFolders>
'@
Set-Content -Path (Join-Path $Target 'LoadFolders.xml') -Value $loadFolders -Encoding UTF8

Write-Host "Deployed to: $Target"
Write-Host ""
Write-Host "Top level:"
Get-ChildItem $Target | Select-Object Name, Mode | Format-Table -AutoSize
Write-Host "File count: $((Get-ChildItem $Target -Recurse -File).Count)"
