@echo off
REM ****************************************************************
REM * Batch deploy script - copies all files & dirs.
REM * from this folder (minus this .bat) into:
REM *    C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z
REM * Then unblocks copied files and launches CastleMinerZ.exe.
REM ****************************************************************

SETLOCAL

REM ----------------------------------------
REM 1) If the game is running, shut it down.
REM ----------------------------------------
echo Checking for running CastleMinerZ.exe...
:CheckRunning
tasklist /FI "IMAGENAME eq CastleMinerZ.exe" 2>NUL | find /I "CastleMinerZ.exe" >NUL
if %ERRORLEVEL%==0 (
    echo Game is running - attempting to close...
    taskkill /IM "CastleMinerZ.exe" /T /F >NUL 2>&1

    echo Waiting for CastleMinerZ.exe to exit...
    :WaitForExit
    timeout /T 1 /NOBREAK >NUL
    tasklist /FI "IMAGENAME eq CastleMinerZ.exe" 2>NUL | find /I "CastleMinerZ.exe" >NUL
    if %ERRORLEVEL%==0 goto WaitForExit

    echo Process terminated.
) else (
    echo No running process found.
)

REM ---------------------------------------------------------------
REM 2) Determine SOURCE folder (where this script lives) & strip \.
REM ---------------------------------------------------------------
SET "SOURCE=%~dp0"
IF "%SOURCE:~-1%"=="\" SET "SOURCE=%SOURCE:~0,-1%"

REM -----------------------------
REM 3) Define DESTINATION folder.
REM -----------------------------
SET "DEST=C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z"

echo.
echo ============================================================
echo Copying from "%SOURCE%" to "%DEST%" (excluding "%~nx0")
echo ============================================================

REM --------------------------------------------------
REM 4) Run ROBOCOPY to sync everything (quiet mode).
REM --------------------------------------------------
robocopy "%SOURCE%" "%DEST%" /E /COPY:DAT /R:3 /W:1 /XF "%~nx0" /NFL /NDL /NJH /NJS /NC /NS /NP

REM ------------------------------------------
REM 5) Check exit code: 0-7 = OK, 8+ = errors.
REM ------------------------------------------
IF ERRORLEVEL 8 (
    echo Deployment finished with errors ^(code=%ERRORLEVEL%^).
    echo.
) ELSE (
    echo Deployment succeeded.
    echo.

    REM ------------------------------------------------------------
    REM 6) Unblock copied files.
    REM ------------------------------------------------------------
    REM Windows may mark downloaded ZIP contents as blocked.
    REM Blocked DLLs can prevent ModLoader from loading mods and may
    REM cause FileLoadException / HRESULT: 0x80131515.
    REM
    REM This removes the Zone.Identifier mark from files copied into
    REM the CastleMiner Z folder.
    echo ============================================================
    echo Unblocking copied files in "%DEST%"
    echo ============================================================

    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Get-ChildItem -LiteralPath $env:DEST -Recurse -Force -File -ErrorAction Stop | Unblock-File -ErrorAction Stop; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"

    IF ERRORLEVEL 1 (
        echo.
        echo WARNING: Failed to unblock one or more files.
        echo If mods fail to load with FileLoadException / HRESULT: 0x80131515,
        echo right-click the downloaded ZIP, choose Properties, check Unblock,
        echo then extract and copy the files again.
        echo.
    ) ELSE (
        echo Unblock completed.
        echo.
    )

    REM -----------------------------
    REM 7) Launch CastleMinerZ.exe.
    REM -----------------------------
    echo Launching CastleMinerZ...
    START "" "%DEST%\CastleMinerZ.exe"
    echo.
)

REM ---------------------
REM 8) Clean up and wait.
REM ---------------------
ENDLOCAL
:: PAUSE