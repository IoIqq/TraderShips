@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

REM ============================================================
REM  Trader Ships (Continued) - Steam Workshop upload
REM
REM  Usage:  upload.bat <SteamAccountName>
REM
REM  The password is typed into steamcmd's own prompt.
REM  It is never stored in this file or passed as an argument.
REM ============================================================

REM ---------- work from the script's own folder ----------
REM Double-clicking a .bat runs it with the current directory set to wherever
REM the shell happened to be (often C:\WINDOWS\system32), so relative paths
REM break. Force the working directory to the script location.
cd /d "%~dp0"

set "STEAMCMD=H:\steamcmd\steamcmd.exe"
set "PY=C:\Users\Adm\AppData\Roaming\dsh-desktop\harness\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe"
set "REPO=E:\AI\mods\TraderShips"
set "VDF=%REPO%\workshop\workshopitem.vdf"
set "MODDIR=H:\steam\steamapps\common\RimWorld\Mods\TraderShipsDev"
set "DLL=%MODDIR%\1.7\Assemblies\TraderShips.dll"
set "LOG=H:\steamcmd\logs\workshop_log.txt"

REM ---------- account name: argument, or prompt when double-clicked ----------
set "ACCOUNT=%~1"

REM Validate the argument in open code, not inside an if-block: %VAR% is
REM expanded when a block is parsed, so a value set by "set /p" inside the
REM same block would still read as empty there.
if defined ACCOUNT goto have_account

echo.
echo ============================================================
echo   Trader Ships - Steam Workshop Upload
echo ============================================================
echo.
echo Enter your Steam account name ^(the login name, not the
echo display name^). Leave blank to cancel.
echo.
set /p "ACCOUNT=Steam account: "

:have_account
REM set /p assigns a single space when it receives an empty line, so the value
REM can be "defined" and yet contain no real account name. Strip spaces before
REM testing.
set "ACCOUNT=!ACCOUNT: =!"
if not defined ACCOUNT goto no_account
if "!ACCOUNT!"=="" goto no_account
goto account_ok

:no_account
echo.
echo Cancelled - no account name given.
echo.
pause
exit /b 1

:account_ok

echo.
echo ============================================================
echo   Trader Ships - Workshop Upload
echo   account: %ACCOUNT%
echo ============================================================
echo.

REM ---------- 1. regenerate the VDF from the description file ----------
echo [1/7] Regenerating workshop VDF from workshop-description.txt ...
if not exist "%PY%" (
    echo   SKIP: python not found at %PY%
    echo   Using the existing VDF as-is.
) else (
    "%PY%" "%REPO%\tools\make-vdf.py" >nul 2>&1
    if errorlevel 1 (
        echo   FAIL: make-vdf.py errored.
        pause
        exit /b 1
    )
    echo   OK: %VDF%
)

REM ---------- 2. full pre-flight ----------
echo.
echo [2/7] Running pre-flight checks ...
if exist "%PY%" if exist "%REPO%\tools\preflight.py" (
    "%PY%" "%REPO%\tools\preflight.py"
    if errorlevel 1 (
        echo.
        echo   FAIL: pre-flight reported problems. Nothing was uploaded.
        pause
        exit /b 1
    )
) else (
    echo   SKIP: preflight.py not available
)

REM ---------- 3. steamcmd ----------
echo.
echo [3/7] Checking steamcmd ...
if not exist "%STEAMCMD%" (
    echo   FAIL: not found at %STEAMCMD%
    pause
    exit /b 1
)
echo   OK

REM ---------- 4. VDF ----------
echo.
echo [4/7] Checking VDF ...
if not exist "%VDF%" (
    echo   FAIL: not found at %VDF%
    pause
    exit /b 1
)
echo   OK

REM ---------- 5. DLL + GUID cache scan ----------
echo.
echo [5/7] Checking assemblies ...
if not exist "%DLL%" (
    echo   FAIL: DLL not found at %DLL%
    echo   Build and deploy first.
    pause
    exit /b 1
)
set "GUIDCOUNT=0"
for %%F in ("%MODDIR%\1.7\Assemblies\*.dll") do (
    echo %%~nxF | findstr /b /i "GUID_" >nul
    if !errorlevel! equ 0 (
        echo   CACHE DLL FOUND: %%~nxF
        set /a GUIDCOUNT+=1
    )
)
if !GUIDCOUNT! gtr 0 (
    echo.
    echo   FAIL: !GUIDCOUNT! GUID cache DLL^(s^) in the Assemblies folder.
    echo   RimWorld creates these at runtime. Shipping them makes subscribers
    echo   load several conflicting copies of the same patches.
    echo   They are safe to delete and get regenerated on next launch.
    pause
    exit /b 1
)
echo   OK: one DLL, no cache copies

REM ---------- 6. report item mode ----------
echo.
echo [6/7] Workshop item mode ...
if exist "%MODDIR%\About\PublishedFileId.txt" (
    set /p EXISTID=<"%MODDIR%\About\PublishedFileId.txt"
    echo   UPDATE existing item: !EXISTID!
) else (
    echo   CREATE new item ^(PublishedFileId.txt is absent^)
)

REM ---------- 7. upload ----------
echo.
echo [7/7] Uploading ...
echo ------------------------------------------------------------
REM Per the steamcmd docs, a command with arguments is best quoted as a whole:
REM   steamcmd "+workshop_build_item C:\path with space\item.vdf"
REM Our VDF path has no spaces, but quoting the whole command keeps it correct
REM if the repo is ever moved to such a path.
"%STEAMCMD%" +login "%ACCOUNT%" "+workshop_build_item %VDF%" +quit
set "RC=%ERRORLEVEL%"
echo ------------------------------------------------------------
echo steamcmd exit code: %RC%
echo.

REM ---------- verify against the LOG, not the exit code ----------
REM steamcmd's exit code does not reliably indicate success, so the real test
REM is the log line "Upload finished for workshop item <ID> : OK".
echo --- verifying against the workshop log ---
set "NEWID="
set "SUCCESS=0"
if exist "%LOG%" (
    for /f "usebackq delims=" %%L in (`powershell -NoProfile -Command "$m = Select-String -Path '%LOG%' -Pattern 'Upload finished for workshop item (\d+) : OK' | Select-Object -Last 1; if ($m) { $m.Matches[0].Groups[1].Value }"`) do (
        set "NEWID=%%L"
        set "SUCCESS=1"
    )
)

if "!SUCCESS!"=="1" (
    echo   SUCCESS: item !NEWID! uploaded.
    echo.
    echo --- recent log lines ---
    powershell -NoProfile -Command "Select-String -Path '%LOG%' -Pattern 'Create new workshop item|Upload starting for workshop|Uploaded new content|Uploaded new workshop preview|Upload finished for workshop item' | Select-Object -Last 8 | ForEach-Object { '  ' + $_.Line }"
) else (
    echo   NOT CONFIRMED: no 'Upload finished ... : OK' line found.
    echo   The upload may have failed. Full log tail below.
    echo.
    powershell -NoProfile -Command "if (Test-Path '%LOG%') { Get-Content '%LOG%' -Tail 15 | ForEach-Object { '  ' + $_ } } else { '  no workshop_log.txt' }"
)

REM ---------- next step ----------
echo.
if exist "%MODDIR%\About\PublishedFileId.txt" (
    echo This run UPDATED the existing item - no duplicate created.
) else (
    if "!SUCCESS!"=="1" (
        echo ============================================================
        echo  This run CREATED a new item: !NEWID!
        echo ============================================================
        echo  Writing PublishedFileId.txt to both locations...
        echo !NEWID!>"%MODDIR%\About\PublishedFileId.txt"
        echo !NEWID!>"%REPO%\About\PublishedFileId.txt"
        echo    "%MODDIR%\About\PublishedFileId.txt"
        echo    "%REPO%\About\PublishedFileId.txt"
        echo.
        echo  Done - future uploads will update this item instead of
        echo  creating duplicates.
        echo ============================================================
    ) else (
        echo ============================================================
        echo  NEXT STEP - the ID could not be read automatically
        echo ============================================================
        echo  Find this line in the log:
        echo    Upload finished for workshop item ^<ID^> : OK
        echo.
        echo  Then write the number to BOTH files:
        echo    echo ^<ID^>^>"%MODDIR%\About\PublishedFileId.txt"
        echo    echo ^<ID^>^>"%REPO%\About\PublishedFileId.txt"
        echo.
        echo  Without it the NEXT upload creates a duplicate workshop item.
        echo ============================================================
    )
)
echo.
pause
endlocal
