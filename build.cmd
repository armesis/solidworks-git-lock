@echo off
rem Builds bin\SwGitLock.dll against this PC's SOLIDWORKS install.
rem Usage: build.cmd ["C:\path\to\SOLIDWORKS"]
setlocal
set "SW=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
if not "%~1"=="" set "SW=%~1"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%SW%\SolidWorks.Interop.sldworks.dll" (
  echo SOLIDWORKS API files not found in "%SW%".
  echo Pass your SOLIDWORKS folder: build.cmd "D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
  exit /b 1
)

cd /d "%~dp0"
if not exist bin mkdir bin

"%CSC%" /nologo /target:library /platform:x64 /optimize+ /out:bin\SwGitLock.dll ^
  /r:"%SW%\SolidWorks.Interop.sldworks.dll" ^
  /r:"%SW%\SolidWorks.Interop.swconst.dll" ^
  /r:"%SW%\SolidWorks.Interop.swpublished.dll" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 (
  echo Build FAILED.
  exit /b 1
)

rem RegAsm needs the referenced SOLIDWORKS interop assemblies next to the add-in.
for %%F in (sldworks swconst swpublished) do copy /y "%SW%\SolidWorks.Interop.%%F.dll" bin\ >nul
echo Built bin\SwGitLock.dll
