@echo off
setlocal

REM --- paths relative to this .bat file ---
set "TOOL=%~dp0FbxToXnb.exe"

REM --- require a dropped FBX ---
if "%~1"=="" (
  echo Drag one or more .fbx files onto this .bat.
  echo.
  pause
  exit /b 1
)

REM --- run for each dropped file ---
:loop
if "%~1"=="" goto done

echo === Building: %~1
"%TOOL%" "%~1"
if errorlevel 1 (
  echo ! Build failed for: %~1
)

shift
goto loop

:done
echo.
pause
endlocal