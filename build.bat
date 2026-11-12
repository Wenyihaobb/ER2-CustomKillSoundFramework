@echo off
setlocal
rem Build and install Custom Kill Sound Framework. Requires the .NET 6 SDK.
rem Priority: ER2_DOTNET env var -> optional private SDK -> "dotnet" on PATH.
rem If you keep several SDKs installed, point at the one you want:
rem   set ER2_DOTNET=C:\path\to\dotnet.exe
set "SDK=%ER2_DOTNET%"
if defined SDK goto :build
if exist "%USERPROFILE%\.dotnet6sdk\dotnet.exe" (
  set "SDK=%USERPROFILE%\.dotnet6sdk\dotnet.exe"
  goto :build
)
set "SDK=dotnet"

:build
"%SDK%" build "%~dp0CustomKillSoundFramework.csproj" -c Release -p:InstallToGame=true
if errorlevel 1 (
  echo Build FAILED. See errors above.
  echo Check Directory.Build.props - copy it from Directory.Build.props.example.
) else (
  echo Build OK. DLL and cfg template installed.
  echo Put your own .wav files into:
  echo   BepInEx\plugins\Custom Kill Sound Framework\Sounds
  echo Name them headshot / kill / hit, optionally followed by a number. No "sound" suffix.
)
pause
