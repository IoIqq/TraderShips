param(
    [string]$OutDir = "E:\AI\mods\rimworld\TraderShips\_test-results"
)

$ErrorActionPreference = "Continue"
$logDir = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs"
$playerLog = Join-Path $logDir "Player.log"

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$report = Join-Path $OutDir "report-$stamp.txt"

Copy-Item $playerLog (Join-Path $OutDir "Player-$stamp.log") -Force -ErrorAction SilentlyContinue

$sb = New-Object System.Text.StringBuilder
function Add-Line($s) { [void]$sb.AppendLine($s) }

Add-Line "=== TraderShips autotest report $stamp ==="
Add-Line ""

$lines = Get-Content $playerLog -ErrorAction SilentlyContinue
Add-Line "Player.log lines: $($lines.Count)"

# --- 1. our init ran
$gen = $lines | Select-String -Pattern "\[TraderShips\] Generated (\d+) themed trade ships for (\d+) factions"
if ($gen) { Add-Line "PASS  generator: $($gen[0].Line -replace '.*\[TraderShips\] ','')" }
else { Add-Line "FAIL  generator did not log a result" }

# --- 2. exceptions referencing our namespace
$ours = $lines | Select-String -Pattern "TraderShips" | Where-Object { $_.Line -match "Exception|ERROR" }
if ($ours) {
    Add-Line "FAIL  $($ours.Count) exception/error lines mention TraderShips:"
    $ours | Select-Object -First 20 | ForEach-Object { Add-Line ("      " + $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length))) }
} else { Add-Line "PASS  no exceptions mention TraderShips" }

# --- 3. our xml patches
$patchFail = $lines | Select-String -Pattern "FactionShips.xml|DispatchTower.xml|PlayerShip.xml"
if ($patchFail) {
    Add-Line "FAIL  $($patchFail.Count) lines reference our patch files:"
    $patchFail | Select-Object -First 10 | ForEach-Object { Add-Line ("      " + $_.Line) }
} else { Add-Line "PASS  no patch-file errors" }

# --- 4. defs resolved
$defChecks = @(
    "TraderShipsDispatchTower",
    "TraderShipsPlayerShip",
    "TraderShipsPlayerShipDef",
    "TraderShipsPlayerShipWorldObject"
)
foreach ($d in $defChecks) {
    $hit = $lines | Select-String -Pattern ("No .* named " + $d + "|Could not find " + $d + "|" + $d + " .* not found")
    if ($hit) { Add-Line "FAIL  def $d missing: $($hit[0].Line)" }
    else { Add-Line "PASS  no missing-def warning for $d" }
}

# --- 5. reflection fallback
$refl = $lines | Select-String -Pattern "Could not resolve SingleDef.thingDef"
if ($refl) { Add-Line "FAIL  reflection on StockGenerator_SingleDef.thingDef failed" }
else { Add-Line "PASS  reflection on StockGenerator_SingleDef.thingDef OK" }

# --- 6. runtime probes (written by mod into dev log dir)
$devLogDir = "H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev\_autotest"
$probe = Join-Path $devLogDir "probe.log"
if (Test-Path $probe) {
    Add-Line ""
    Add-Line "--- runtime probe.log ---"
    Get-Content $probe | ForEach-Object { Add-Line $_ }
}

Add-Line ""
Add-Line "=== all WARN/ERROR from our namespace or about our defs ==="
$lines | Select-String -Pattern "TraderShips" | Where-Object { $_.Line -match "WARN|ERROR" } |
    Select-Object -First 40 | ForEach-Object { Add-Line $_.Line }

$sb.ToString() | Set-Content -Path $report -Encoding UTF8
Write-Host $sb.ToString()
Write-Host ""
Write-Host "Report: $report" -ForegroundColor Green
