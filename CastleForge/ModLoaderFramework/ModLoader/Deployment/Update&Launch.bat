@echo off
REM ****************************************************************
REM * Batch deploy script - copies all files & dirs.
REM * from this folder (minus this .bat) into:
REM *    C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z
REM * Then launches CastleMinerZ.exe.
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
robocopy "%SOURCE%" "%DEST%" /E /COPYALL /R:3 /W:1 /XF "%~nx0" /NFL /NDL /NJH /NJS /NC /NS /NP

REM ------------------------------------------
REM 5) Check exit code: 0-7 = OK, 8+ = errors.
REM ------------------------------------------
IF %ERRORLEVEL% GEQ 8 (
    echo Deployment finished with errors ^(code=%ERRORLEVEL%^).
    echo.
) ELSE (
    echo Deployment succeeded.
    echo Launching CastleMinerZ...
    START "" "%DEST%\CastleMinerZ.exe"
    echo.
)

REM ---------------------
REM 6) Clean up and wait.
REM ---------------------
ENDLOCAL
:: PAUSE