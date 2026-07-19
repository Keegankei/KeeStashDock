@echo off
rem ============================================================================
rem  DebugFile-Clean.bat - Delete generated build folders below the solution.
rem
rem  Usage:
rem    DebugFile-Clean.bat       Removes bin and obj folders.
rem    DebugFile-Clean.bat /all  Also removes Visual Studio .vs folders.
rem
rem  Close Visual Studio before using /all. Text is ASCII for cmd compatibility.
rem ============================================================================
setlocal EnableExtensions

pushd "%~dp0" || (echo [ERROR] Cannot enter the script folder. & exit /b 1)

set "CLEAN_VS=0"
if /i "%~1"=="/all" set "CLEAN_VS=1"

set /a COUNT=0
echo Scanning: %CD%
echo.

for /d /r %%d in (bin obj) do (
    if exist "%%d" (
        echo   rd "%%d"
        rd /s /q "%%d"
        set /a COUNT+=1
    )
)

if "%CLEAN_VS%"=="1" (
    for /d /r %%d in (.vs) do (
        if exist "%%d" (
            echo   rd "%%d"
            rd /s /q "%%d"
            set /a COUNT+=1
        )
    )
)

echo.
echo Done. Removed %COUNT% folder(s).
popd
exit /b 0
