# Trader Ships (Continued) - Steam Workshop upload helper
#
# Implements the pre-flight checks required by the RimWorld publishing guide (4.3):
#   1. steamcmd exists
#   2. VDF exists
#   3. mod DLL exists
#   4. no GUID_*.dll cache copies in the deploy folder (the biggest workshop hazard)
#   5. report exit code, log tail, and PublishedFileId state
#
# The Steam password is entered interactively by steamcmd and never handled here.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\upload-workshop.ps1 -Account <name>

param(
    [Parameter(Mandatory = $true)]
    [string]$Account
)

$ErrorActionPreference = 'Stop'

$SteamCmd = 'H:\steamcmd\steamcmd.exe'
$Vdf      = 'E:\AI\mods\TraderShips\workshop\workshopitem.vdf'
$ModDir   = 'H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev'
$Dll      = Join-Path $ModDir '1.7\Assemblies\TraderShips.dll'
$LogFile  = 'H:\steamcmd\logs\workshop_log.txt'

function Fail($msg) {
    Write-Host "  FAIL: $msg" -ForegroundColor Red
    exit 1
}

Write-Host '[1/6] Running full pre-flight check...'
$python = 'C:\Users\Adm\AppData\Roaming\dsh-desktop\harness\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
$preflight = 'E:\AI\mods\TraderShips\tools\preflight.py'
if ((Test-Path $python) -and (Test-Path $preflight)) {
    & $python $preflight
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Fail 'pre-flight check failed - fix the FAIL lines above before uploading.'
    }
} else {
    Write-Host '  SKIP: python or preflight.py not found' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '[2/6] Checking steamcmd...'
if (-not (Test-Path $SteamCmd)) { Fail "steamcmd not found at $SteamCmd" }
Write-Host "  OK: $SteamCmd" -ForegroundColor Green

Write-Host '[3/6] Checking VDF...'
if (-not (Test-Path $Vdf)) { Fail "VDF not found at $Vdf`n  Run: python E:\AI\mods\TraderShips\tools\make-vdf.py" }
Write-Host "  OK: $Vdf" -ForegroundColor Green

Write-Host '[4/6] Checking mod DLL...'
if (-not (Test-Path $Dll)) { Fail "DLL not found at $Dll`n  Build and deploy first." }
Write-Host "  OK: $Dll" -ForegroundColor Green

Write-Host '[5/6] Scanning for GUID cache DLLs...'
$asmDir = Join-Path $ModDir '1.7\Assemblies'
$cache = @(Get-ChildItem -Path $asmDir -Filter 'GUID_*.dll' -ErrorAction SilentlyContinue)
$allDll = @(Get-ChildItem -Path $asmDir -Filter '*.dll' -ErrorAction SilentlyContinue)
if ($cache.Count -gt 0) {
    $cache | ForEach-Object { Write-Host "  FOUND CACHE: $($_.Name)" -ForegroundColor Red }
    Fail ("$($cache.Count) GUID cache DLL(s) present. RimWorld generates these at runtime; " +
          "shipping them makes subscribers load several conflicting assembly copies. " +
          "Delete them (safe, regenerated on next launch) and retry.")
}
Write-Host "  OK: $($allDll.Count) DLL(s), no GUID cache" -ForegroundColor Green
$allDll | ForEach-Object { Write-Host "      $($_.Name)" }

Write-Host '[6/6] Uploading...'
Write-Host ''
& $SteamCmd +login $Account +workshop_build_item $Vdf +quit
$rc = $LASTEXITCODE
Write-Host ''
Write-Host "steamcmd exit code: $rc"

Write-Host ''
Write-Host '--- Workshop log tail ---'
if (Test-Path $LogFile) {
    Select-String -Path $LogFile -Pattern 'Upload finished for workshop item|Create new workshop item|Uploaded new content|ERROR' |
        Select-Object -Last 8 |
        ForEach-Object { Write-Host "  $($_.Line)" }
} else {
    Write-Host '  (no workshop_log.txt yet)'
}

Write-Host ''
Write-Host '--- PublishedFileId.txt status ---'
$idInMod  = Join-Path $ModDir 'About\PublishedFileId.txt'
$idInRepo = 'E:\AI\mods\TraderShips\About\PublishedFileId.txt'
if (Test-Path $idInMod) {
    Write-Host "  existing ID: $((Get-Content $idInMod -Raw).Trim())  (this upload UPDATED that item)"
    Write-Host '  OK: no duplicate item created.' -ForegroundColor Green
} else {
    Write-Host '  NOT PRESENT - this upload created a NEW workshop item.' -ForegroundColor Yellow
    Write-Host '  Take the numeric ID from the log line "Upload finished for workshop item <ID>"'
    Write-Host '  and write it to BOTH files:'
    Write-Host "    $idInMod"
    Write-Host "    $idInRepo"
    Write-Host '  Without it, the NEXT upload will create yet another duplicate item.'
}

exit $rc
