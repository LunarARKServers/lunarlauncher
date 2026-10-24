@echo off
taskkill /F /IM LunarLauncher.exe >nul 2>&1
set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set WPF="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"

%CSC% /target:winexe /out:"%~dp0LunarLauncher.exe" /optimize+ ^
  /r:%WPF%\PresentationFramework.dll ^
  /r:%WPF%\PresentationCore.dll ^
  /r:%WPF%\WindowsBase.dll ^
  /r:System.Xaml.dll ^
  /win32icon:"E:\LunarARKServers\Logo\logo_turkis_white_dragon_win.ico" ^
  /resource:"%~dp0bg.jpg",bg.jpg ^
  "%~dp0LunarLauncher.cs"

if %errorlevel% equ 0 (
    echo Built LunarLauncher.exe successfully.
) else (
    echo Build failed.
    exit /b 1
)
