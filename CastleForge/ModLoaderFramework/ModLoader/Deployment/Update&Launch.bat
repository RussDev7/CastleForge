@echo off
REM ****************************************************************
REM * Batch deploy script - copies modded files only.
REM * from this folder into:
REM *    C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z
REM * Then unblocks copied files and launches CastleMinerZ.exe.
REM *
REM * This intentionally copies only:
REM *   - CastleMinerZ.exe.config
REM *   - ModLoader.dll
REM *   - !Mods\**
REM *
REM * It does NOT copy the whole directory the .bat is in.
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
echo Copying modded files only from "%SOURCE%" to "%DEST%"
echo ============================================================

REM --------------------------------------------------
REM 4) Copy only known modded root files and !Mods.
REM --------------------------------------------------
SET "DEPLOY_ERROR=0"

IF NOT EXIST "%DEST%\" (
    echo ERROR: Destination folder does not exist:
    echo "%DEST%"
    echo.
    SET "DEPLOY_ERROR=1"
    GOTO DeployDone
)

REM Copy root mod files only.
IF EXIST "%SOURCE%\CastleMinerZ.exe.config" (
    copy /Y "%SOURCE%\CastleMinerZ.exe.config" "%DEST%\CastleMinerZ.exe.config" >NUL
    IF ERRORLEVEL 1 SET "DEPLOY_ERROR=1"
) ELSE (
    echo WARNING: Missing CastleMinerZ.exe.config - skipping.
)

IF EXIST "%SOURCE%\ModLoader.dll" (
    copy /Y "%SOURCE%\ModLoader.dll" "%DEST%\ModLoader.dll" >NUL
    IF ERRORLEVEL 1 SET "DEPLOY_ERROR=1"
) ELSE (
    echo WARNING: Missing ModLoader.dll - skipping.
)

REM Copy the mod folder only.
IF EXIST "%SOURCE%\!Mods\" (
    robocopy "%SOURCE%\!Mods" "%DEST%\!Mods" /E /COPY:DAT /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP

    REM Robocopy exit code: 0-7 = OK, 8+ = errors.
    IF ERRORLEVEL 8 SET "DEPLOY_ERROR=1"
) ELSE (
    echo WARNING: Missing !Mods folder - skipping.
)

:DeployDone

REM ------------------------------------------
REM 5) Check exit code: 0-7 = OK, 8+ = errors.
REM ------------------------------------------
IF "%DEPLOY_ERROR%"=="1" (
    echo Deployment finished with errors.
    echo.
) ELSE (
    echo Deployment succeeded.
    echo.

    REM ------------------------------------------------------------
    REM 6) Unblock copied mod binaries only.
    REM ------------------------------------------------------------
    REM Windows may mark downloaded ZIP contents as blocked.
    REM Blocked DLLs can prevent ModLoader from loading mods and may
    REM cause FileLoadException / HRESULT: 0x80131515.
    REM
    REM Only scan:
    REM   - ModLoader.dll
    REM   - !Mods\**\*.dll
    REM   - !Mods\**\*.exe
    REM
    REM Other formats like PNG, XNB, JSON, INI, TXT, and MD are data/assets
    REM and normally do not need to be unblocked.
    echo ============================================================
    echo Unblocking ModLoader.dll and mod DLL/EXE files only
    echo Target: "%DEST%"
    echo ============================================================

    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $modLoader = Join-Path $env:DEST 'ModLoader.dll'; if (Test-Path -LiteralPath $modLoader) { Get-Item -LiteralPath $modLoader -Force | Unblock-File -ErrorAction Stop }; $mods = Join-Path $env:DEST '!Mods'; if (Test-Path -LiteralPath $mods) { Get-ChildItem -LiteralPath $mods -Recurse -Force -File -ErrorAction Stop | Where-Object { $_.Extension -ieq '.dll' -or $_.Extension -ieq '.exe' } | Unblock-File -ErrorAction Stop }; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"

    IF ERRORLEVEL 1 (
        echo.
        echo WARNING: Failed to unblock one or more mod binaries.
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
