@echo off
setlocal
if not exist "%~dp0app\Glimpse.exe" goto missing
if not exist "%~dp0app\Glimpse.dll" goto missing
if not exist "%~dp0app\coreclr.dll" goto missing
if not exist "%~dp0app\ffmpeg.exe" goto missing
set "GLIMPSE_FFMPEG=%~dp0app\ffmpeg.exe"
start "Glimpse" /D "%~dp0app" "%~dp0app\Glimpse.exe"
exit /b 0
:missing
echo Extraia TODOS os arquivos do ZIP para uma pasta antes de abrir o Glimpse.
echo Mantenha as pastas app, scripts e licenses junto deste arquivo.
pause
exit /b 1
