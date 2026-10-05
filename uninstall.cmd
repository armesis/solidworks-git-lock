@echo off
rem Unregisters the Git Lock add-in. Close SOLIDWORKS first.

net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /nologo /unregister "%~dp0bin\SwGitLock.dll"
echo Uninstalled.
pause
