@echo off
setlocal
set "APP=%~dp0TingGeRiZhi.App\bin\Debug\net10.0-windows10.0.26100.0\聆迹.exe"
if not exist "%APP%" (
  echo 尚未找到已编译的听歌日志，请先运行 README.md 中的构建命令。
  pause
  exit /b 1
)
start "听歌日志" "%APP%"
