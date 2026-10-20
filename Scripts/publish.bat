@echo off
setlocal
pushd "%~dp0.."
dotnet publish src\GenshinWishTracker.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o WEB
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Review the errors above; no successful publish is confirmed.
  popd
  exit /b 1
)
move /Y WEB\GenshinWishTracker.exe GenshinWishTracker.exe >nul
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Could not place GenshinWishTracker.exe in the repository root.
  popd
  exit /b 1
)
echo.
echo Publish succeeded. The EXE is in the repository root; support files are in WEB.
popd
