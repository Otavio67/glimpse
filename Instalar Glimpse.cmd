@echo off
setlocal
echo Instalando o Glimpse. Aguarde a verificacao dos arquivos.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-App.ps1"
if errorlevel 1 (
  echo.
  echo Nao foi possivel concluir. Confira a mensagem acima e tente novamente.
  pause
  exit /b 1
)
exit /b 0
