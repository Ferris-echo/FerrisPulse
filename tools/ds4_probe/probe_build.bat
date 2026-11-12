@echo off
chcp 65001 >nul
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /target:exe /codepage:65001 /out:Ds4Probe.exe Ds4Probe.cs "..\FerrisPulse\src\Hardware\HidTransport.cs"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo [OK] Ds4Probe.exe built
endlocal
