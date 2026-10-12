@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" %*
set "TaskExitCode=%errorlevel%"
if not "%TaskExitCode%"=="0" pause
exit /b %TaskExitCode%
