# Trader Ships - deploy 1.7 build to the RimWorld Mods folder.
# Pure-ASCII on purpose: Windows PowerShell 5.1 misreads UTF-8 files without a BOM.
#
# IMPORTANT: this script rebuilds $Target from scratch each run. Copy-Item -Recurse into an
# existing directory nests the source folder inside it (producing 1.7/1.7/), so we always
# start from a clean destination.
param(
    [Parameter(Mandatory = $false)]
    [string]$Source = 'E:\AI\mods\rimworld\TraderShips',
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

# About: ship About.xml as-is. It now declares every supported version (1.1~1.7);
# overwriting it with About-1.7.xml would drop that and make older versions unloadable.
Copy-Tree (Join-Path $Source 'About') (Join-Path $Target 'About')

# Do NOT ship a PublishedFileId.txt unless the source has one. A stale/original ID here makes
# ModUpdater overwrite someone else's workshop entry instead of creating our own.
if (-not (Test-Path (Join-Path $Source 'About\PublishedFileId.txt'))) {
    Remove-Item (Join-Path $Target 'About\PublishedFileId.txt') -Force -ErrorAction SilentlyContinue
}

# Versioned content: every 1.x folder that exists gets shipped, so old RimWorld versions
# keep working. 1.7 is the only one we build here, but 1.1~1.5 hold the original binaries.
foreach ($ver in @('1.1', '1.2', '1.3', '1.4', '1.5', '1.6', '1.7')) {
    $verSrc = Join-Path $Source $ver
    if (-not (Test-Path $verSrc)) { continue }

    Copy-Tree $verSrc (Join-Path $Target $ver)

    # Only compiled dlls belong in Assemblies
    $asm = Join-Path $Target "$ver\Assemblies"
    if (Test-Path $asm) {
        Get-ChildItem $asm -Filter '*.deps.json' -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem $asm -Filter '*.csproj*' -ErrorAction SilentlyContinue | Remove-Item -Force
    }
}

# Version-independent shared assets (copied flat, no nesting)
foreach ($shared in @('Textures', 'Sounds', 'Languages', 'Patches')) {
    $src = Join-Path $Source $shared
    if (Test-Path $src) {
        Copy-Tree $src (Join-Path $Target $shared)
    }
}

# LoadFolders: copy the real file from the source tree.
# It declares one <vX.Y> block per supported version, each mounting "/" plus its own folder.
# Hardcoding a single <v1.7> block here (as this script used to) would silently drop 1.1~1.6.
$lf = Join-Path $Source 'LoadFolders.xml'
if (-not (Test-Path $lf)) { throw "Missing LoadFolders.xml in source: $lf" }
Copy-Item $lf (Join-Path $Target 'LoadFolders.xml') -Force

Write-Host "Deployed to: $Target"
Write-Host ""
Write-Host "Top level:"
Get-ChildItem $Target | Select-Object Name, Mode | Format-Table -AutoSize
Write-Host "File count: $((Get-ChildItem $Target -Recurse -File).Count)"
