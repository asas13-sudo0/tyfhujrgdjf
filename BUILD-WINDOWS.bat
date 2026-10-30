@echo off
setlocal
where msbuild >nul 2>nul
if errorlevel 1 (
  echo MSBuild was not found. Run this from a Visual Studio Developer Command Prompt.
  exit /b 1
)
msbuild "%~dp0Nekogiri.sln" /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /m
if errorlevel 1 exit /b %errorlevel%
echo.
echo Build complete: %~dp0KirigiriNetworkConnectMod\bin\Release\Nekogiri.dll
