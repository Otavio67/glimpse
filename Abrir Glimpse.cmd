@echo off
setlocal
set "DOTNET_ROOT=%~dp0.tools\dotnet"
set "DOTNET_ROOT_X64=%DOTNET_ROOT%"
set "GLIMPSE_FFMPEG=%~dp0.tools\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe"
set "GLIMPSE_APP=%~dp0src\Glimpse\bin\Release\net10.0-windows10.0.19041.0\Glimpse.dll"
if not exist "%DOTNET_ROOT%\dotnet.exe" goto missing
if not exist "%GLIMPSE_FFMPEG%" goto missing
if not exist "%GLIMPSE_APP%" goto missing
"%DOTNET_ROOT%\dotnet.exe" "%GLIMPSE_APP%"
if errorlevel 1 pause
exit /b
:missing
call "%~dp0Instalar Glimpse.cmd"
exit /b %errorlevel%
