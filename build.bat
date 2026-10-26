@echo off
chcp 65001 >nul
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

echo =======================================================
echo Compiling FerrisPulse (Open-Source Clean Raw Binary)...
echo Compiler: %CSC%
echo =======================================================

"%CSC%" /target:winexe /optimize+ /codepage:65001 /win32icon:app.ico /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:src\UI\Resources\mouse_body.png,mouse_body.png /out:FerrisPulse.exe /recurse:src\*.cs

if %ERRORLEVEL% neq 0 (
    echo [FAILED] Compilation failed with error code: %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo [SUCCESS] Build successful (Pure Open-Source Raw Binary): FerrisPulse.exe

endlocal
