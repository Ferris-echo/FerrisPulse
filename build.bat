@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo Error: .NET Framework compiler not found at %CSC%
    pause
    exit /b 1
)

echo Compiling FerrisPulse with app.ico...
"%CSC%" /target:winexe /optimize+ /codepage:65001 /win32icon:app.ico /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:FerrisPulse.exe RazerBatteryTray.cs

if %ERRORLEVEL% equ 0 (
    echo Build successful: FerrisPulse.exe
) else (
    echo Build failed with error code: %ERRORLEVEL%
)
