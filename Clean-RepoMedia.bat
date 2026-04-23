@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "MODE=%~1"
if "%MODE%"=="" set "MODE=scan"

set "KEEP_FILE=media-clean-keep.txt"
set "REPORT=media-clean-report.txt"
set /a COUNT=0
set /a TOTAL=0

if /I not "%MODE%"=="scan" if /I not "%MODE%"=="delete" if /I not "%MODE%"=="gitclean" (
    echo Usage: %~nx0 [scan ^| delete ^| gitclean]
    echo.
    echo   scan     = dry run, list matching .png and .gif files
    echo   delete   = permanently delete matching files from the working tree
    echo   gitclean = remove matching files from Git tracking and delete them locally
    echo.
    echo Optional keep-list file: %KEEP_FILE%
    echo Add one relative path per line to preserve a file, for example:
    echo   Assets\Branding\Logo.png
    echo   CastleForge\ModLoaderFramework\ModLoader\_Images\Preview.png
    exit /b 1
)

if not exist "%KEEP_FILE%" (
    >"%KEEP_FILE%" echo # One relative path per line to preserve a file during cleanup.
    >>"%KEEP_FILE%" echo # Example:
    >>"%KEEP_FILE%" echo # Assets\Branding\Logo.png
    >>"%KEEP_FILE%" echo # CastleForge\ModLoaderFramework\ModLoader\_Images\Preview.png
)

if exist "%REPORT%" del /f /q "%REPORT%" >nul 2>&1

set "ROOT=%CD%"
if not "%ROOT:~-1%"=="\" set "ROOT=%ROOT%\"

echo [%MODE%] Repo root: %CD%
echo [%MODE%] Keep-list : %KEEP_FILE%
echo.

for /r %%F in (*.png *.gif) do (
    call :ShouldSkip "%%~fF" SKIP
    if /I not "!SKIP!"=="1" (
        set "REL=%%~fF"
        set "REL=!REL:%ROOT%=!"
        set /a COUNT+=1
        set /a TOTAL+=%%~zF

        if /I "%MODE%"=="scan" (
            echo !REL!
            >>"%REPORT%" echo !REL!
        ) else if /I "%MODE%"=="delete" (
            echo Deleting !REL!
            del /f /q "%%~fF"
        ) else if /I "%MODE%"=="gitclean" (
            echo Removing !REL!
            call :GitRemove "%%~fF"
        )
    )
)

echo.
echo Files matched : !COUNT!
echo Total bytes   : !TOTAL!

if /I "%MODE%"=="scan" (
    echo Report saved : %REPORT%
    echo.
    echo This was a dry run. Nothing was deleted.
    echo.
    choice /C YN /N /M "Proceed with local delete now? [Y/N]: "
    if errorlevel 2 (
        echo.
        echo Aborted. No files were deleted.
        exit /b 0
    )
    echo.
    call "%~f0" delete
    exit /b %errorlevel%
) else (
    echo.
    echo Cleanup finished.
)

pause
exit /b 0

:ShouldSkip
setlocal EnableExtensions EnableDelayedExpansion
set "P=%~1"
set "SKIP=0"

rem Skip common generated/vendor folders.
if /I not "!P:\.git\=!"=="!P!" set "SKIP=1"
if /I not "!P:\bin\=!"=="!P!" set "SKIP=1"
if /I not "!P:\obj\=!"=="!P!" set "SKIP=1"
if /I not "!P:\.vs\=!"=="!P!" set "SKIP=1"
if /I not "!P:\packages\=!"=="!P!" set "SKIP=1"
if /I not "!P:\node_modules\=!"=="!P!" set "SKIP=1"

if exist "%KEEP_FILE%" (
    for /f "usebackq delims=" %%K in ("%KEEP_FILE%") do (
        set "K=%%K"
        if defined K (
            if /I not "!K:~0,1!"=="#" (
                set "K=!K:"=!"
                set "K=!K:/=\!"
                if "!K:~0,1!"=="\" set "K=!K:~1!"

                if defined K (
                    set "KEEPABS=%ROOT%!K!"

                    rem Exact file match
                    if /I "!P!"=="!KEEPABS!" set "SKIP=1"

                    rem Folder match: if keep entry ends with "\" keep everything under it
                    if "!K:~-1!"=="\" (
                        call set "TEST=%%P:!KEEPABS!=%%"
                        if /I not "!TEST!"=="!P!" set "SKIP=1"
                    )
                )
            )
        )
    )
)

endlocal & set "%~2=%SKIP%"
exit /b 0

:GitRemove
setlocal
set "FILE=%~1"
git ls-files --error-unmatch "%FILE%" >nul 2>&1
if not errorlevel 1 git rm -f -- "%FILE%" >nul 2>&1
if exist "%FILE%" del /f /q "%FILE%"
endlocal
exit /b 0
