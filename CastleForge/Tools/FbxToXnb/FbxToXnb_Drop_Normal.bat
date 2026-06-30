@echo off
setlocal

REM --- paths relative to this .bat file ---
set "TOOL=%~dp0FbxToXnb.exe"
set "PIPE=%~dp0SkinedModelProcessor\DNA.SkinnedPipeline.dll"
set "PROC=ScaledModelProcessor"

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
if exist "%PIPE%" (
  REM If TexturePacks [Models] AuthoringLocation / AuthoringRotation are non-identity,
  REM append matching switches:
  REM   --authoringLocation "0,0,0" --authoringRotation "0,0,0"
  "%TOOL%" "%~1" --pipeline "%PIPE%" --processor "%PROC%" --fbxComp 10.0
) else (
  echo ! WARNING: %PIPE% was not found.
  echo ! Falling back to stock ModelProcessor. BarrelTip/socket transforms may not be scaled correctly.
  "%TOOL%" "%~1" --fbxComp 10.0
)
if errorlevel 1 (
  echo ! Build failed for: %~1
)

shift
goto loop

:done
echo.
pause
endlocal
