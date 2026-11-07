@echo off
rem Builds bin\DualSenseBattery.exe with the C# compiler that ships with Windows (.NET Framework 4.8). Nothing to install.
cd /d "%~dp0"
set FW=%windir%\Microsoft.NET\Framework64\v4.0.30319
set WMD=%windir%\System32\WinMetadata
if not exist bin mkdir bin
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:src\app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll ^
  /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" ^
  /r:"%WMD%\Windows.UI.winmd" /r:"%WMD%\Windows.Data.winmd" /r:"%WMD%\Windows.Foundation.winmd" ^
  /out:bin\DualSenseBattery.exe src\DualSenseBattery.cs
if errorlevel 1 (echo. & echo Build failed. & pause & exit /b 1)
echo Built bin\DualSenseBattery.exe