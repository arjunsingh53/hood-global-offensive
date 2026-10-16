@echo off
rem ---------------------------------------------------------------------------
rem  HOODMATCH launcher.
rem
rem  Double-click this file to play. It exists because the .NET SDK on this
rem  machine was installed per-user into %USERPROFILE%\.dotnet, which is not on
rem  the system PATH, so a plain "dotnet run" in a fresh terminal may not find
rem  the SDK at all.
rem
rem  Any arguments are passed straight through, e.g.
rem      play.cmd --headless --seconds 300
rem      play.cmd --screenshot shot.png --map Arena --screen upgrade
rem ---------------------------------------------------------------------------
setlocal
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   Could not find the .NET SDK.
  echo   Expected it in: %DOTNET_ROOT%
  echo.
  echo   Install it with:
  echo     dotnet-install.ps1 -Channel 10.0 -InstallDir "%%USERPROFILE%%\.dotnet"
  echo.
  pause
  exit /b 1
)

echo Starting HOODMATCH...
dotnet run -- %*

rem Keep the window open if the game crashed, so the error is readable.
if errorlevel 1 (
  echo.
  echo   Hood Global Offensive exited with an error ^(see above^).
  pause
)
endlocal
