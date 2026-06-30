@echo off
setlocal

REM --- paths relative to this .bat file ---
set "TOOL=%~dp0FbxToXnb.exe"
set "PIPE=%~dp0SkinedModelProcessor"

REM --- require a dropped FBX ---
if "%~1"=="" (
  echo Drag one or more FBX animation files onto this .bat.
  echo.
  echo The FBX should contain the CastleForge/CMZ avatar armature and one animation take.
  echo Output will be a standalone DNA AnimationClip .xnb.
  echo.
  pause
  exit /b 1
)

REM NOTE: AnimationClipProcessor builds motion data, not model geometry.
REM Do not pass --fbxComp or --param Scale here.

REM --- run for each dropped file ---
:loop
if "%~1"=="" goto done

echo === Building animation clip: %~1
"%TOOL%" --processor AnimationClipProcessor --pipelineDir "%PIPE%" "%~1"
if errorlevel 1 (
  echo ! Build failed for: %~1
)

shift
goto loop

:done
echo.
pause
endlocal
