@echo off
rem Builds PromptPilot.exe with the C# compiler included in Windows. No install needed.
rem Usage: build.bat [output path]
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set OUT=%~1
if "%OUT%"=="" set OUT=%~dp0PromptPilot.exe
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:"%OUT%" /lib:"%FW%\WPF" /win32icon:"%~dp0assets\app.ico" ^
 /resource:"%~dp0assets\app.ico",app.ico /resource:"%~dp0assets\app-off.ico",app-off.ico ^
 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Security.dll ^
 /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll "%~dp0src\*.cs"
