@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start.ps1" %*
set "TaskExitCode=%errorlevel%"
if not "%TaskExitCode%"=="0" pause
exit /b %TaskExitCode%
