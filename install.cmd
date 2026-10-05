@echo off
rem Builds and registers the Git Lock add-in. Close SOLIDWORKS first.
rem Usage: install.cmd ["C:\path\to\SOLIDWORKS"]   (pass a path only if SOLIDWORKS isn't in the default folder;
rem then run it from an administrator command prompt)

net session >nul 2>&1
if errorlevel 1 (
  echo Asking for administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0"
tasklist /fi "imagename eq SLDWORKS.exe" | find /i "SLDWORKS.exe" >nul && (
  echo Please close SOLIDWORKS and run this again.
  pause
  exit /b 1
)

call "%~dp0build.cmd" %1 || (pause & exit /b 1)

"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /nologo /codebase "%~dp0bin\SwGitLock.dll"
if errorlevel 1 (
  echo Registration FAILED.
  pause
  exit /b 1
)

echo.
echo Installed. Start SOLIDWORKS and open the "Git Lock" tab (padlock icon) in the Task Pane on the right.
pause
