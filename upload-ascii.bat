@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

REM ============================================================
REM  Minimal workshop upload: no VDF regeneration, no pre-flight.
REM  Used when the full upload.bat aborts on the "key name too long"
REM  assertion and we need to know whether the VDF itself is the cause.
REM
REM  Usage:  upload-ascii.bat <account> <vdf-path>
REM ============================================================

cd /d "%~dp0"

if exist "H:\RimCrow\tools\steamcmd\steamcmd.exe" (
    set "STEAMCMD=H:\RimCrow\tools\steamcmd\steamcmd.exe"
    set "LOG=H:\RimCrow\tools\steamcmd\logs\workshop_log.txt"
) else (
    set "STEAMCMD=H:\steamcmd\steamcmd.exe"
    set "LOG=H:\steamcmd\logs\workshop_log.txt"
)

set "ACCOUNT=%~1"
if not defined ACCOUNT set "ACCOUNT=rb3481141298"

set "VDF=%~2"
if not defined VDF set "VDF=%CD%\workshop\workshopitem-ascii.vdf"

if not exist "%STEAMCMD%" (
    echo FAIL: steamcmd not found at %STEAMCMD%
    pause
    exit /b 1
)
if not exist "%VDF%" (
    echo FAIL: VDF not found at %VDF%
    pause
    exit /b 1
)

echo ------------------------------------------------------------
echo  VDF      : %VDF%
echo  steamcmd : %STEAMCMD%
echo  account  : %ACCOUNT%
echo ------------------------------------------------------------

set "LOGLINES=0"
if exist "%LOG%" (
    for /f %%C in ('powershell -NoProfile -Command "if (Test-Path '%LOG%') { (Get-Content '%LOG%' | Measure-Object -Line).Lines } else { 0 }"') do set "LOGLINES=%%C"
)

"%STEAMCMD%" +login "%ACCOUNT%" "+workshop_build_item %VDF%" +quit
echo steamcmd exit code: %RC%

echo.
echo Log tail:
powershell -NoProfile -Command "Get-Content '%LOG%' -Tail 12"

echo.
echo Checking for a success line produced by THIS run ...
powershell -NoProfile -Command ^
  "$lines = Get-Content '%LOG%'; $new = $lines | Select-Object -Skip %LOGLINES%; $hit = $new | Where-Object { $_ -match 'Upload finished.*: OK' }; if ($hit) { Write-Host ('SUCCESS: ' + $hit[-1]) -ForegroundColor Green; exit 0 } else { Write-Host 'NO success line in this run' -ForegroundColor Red; exit 1 }"
if errorlevel 1 (
    echo.
    echo Upload did not report OK. See the log above.
    pause
    exit /b 1
)

echo.
echo Done.
pause
