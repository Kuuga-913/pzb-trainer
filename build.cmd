@echo off
rem 需要 .NET 8 SDK（Windows）
setlocal
pushd "%~dp0"
dotnet publish "src\PBZTrainer\PBZTrainer.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o out
if errorlevel 1 (
  echo.
  echo 构建失败，请确认已安装 .NET 8 SDK：https://dotnet.microsoft.com/download
) else (
  echo.
  echo 完成：out\PBZTrainer.exe
  echo 记得把 PBZBridge.dll 放到 out\ 目录下再运行。
)
popd
endlocal
pause
