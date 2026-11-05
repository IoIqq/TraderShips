param(
    [string]$RimWorldExe = "H:\steam\steamapps\common\RimWorld\RimWorld.exe",
    [int]$TimeoutSec = 420
)

$ErrorActionPreference = "Stop"
$logDir = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs"
$playerLog = Join-Path $logDir "Player.log"
$devLogDir = "H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev\_autotest"

Write-Host "== TraderShips autotest ==" -ForegroundColor Cyan

$running = Get-Process -Name RimWorld -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "RimWorld already running (PID $($running.Id)). Close it first." -ForegroundColor Yellow
    exit 2
}

# 1. stale log cleanup
if (Test-Path $playerLog) {
    Move-Item $playerLog (Join-Path $logDir "Player.autotest-prev.log") -Force
}
if (-not (Test-Path $devLogDir)) { New-Item -ItemType Directory -Path $devLogDir | Out-Null }
Get-ChildItem $devLogDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

# 2. launch
Write-Host "Launching RimWorld..." -ForegroundColor Green
$proc = Start-Process -FilePath $RimWorldExe -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()

# 3. wait for Player.log to appear and grow
$lastSize = 0
$stable = 0
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
    Start-Sleep -Milliseconds 1200
    if ($proc.HasExited) { break }
    if (Test-Path $playerLog) {
        $size = (Get-Item $playerLog).Length
        if ($size -eq $lastSize) { $stable++ } else { $stable = 0; $lastSize = $size }
        if ($stable -ge 8) { break }
    }
}

Write-Host "Elapsed: $([int]$sw.Elapsed.TotalSeconds)s  PID: $($proc.Id)  Exited: $($proc.HasExited)"
if (Test-Path $playerLog) {
    Write-Host "Player.log size: $((Get-Item $playerLog).Length) bytes"
}
Write-Host ""
Write-Host "Game should now be at the main menu. Load a save (or start one) and run the checklist." -ForegroundColor Cyan
Write-Host "When done, run: .\tools\collect-autotest.ps1"
