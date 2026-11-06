param([string]$InstallRoot, [string]$DependencyCache, [switch]$NoLaunch, [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$package = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { throw 'Esta versao do Glimpse requer Windows x64.' }
if ([Environment]::OSVersion.Version.Build -lt 19041) { throw 'O Glimpse requer Windows 10 versao 2004 ou mais recente.' }
if (-not $InstallRoot) { $InstallRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Glimpse' }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
if (-not $DependencyCache) { $DependencyCache = Join-Path $package '.tools' }
. (Join-Path $PSScriptRoot 'Install-Dependencies.ps1')
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
$lock = $null
try { $lock = [IO.File]::Open((Join-Path $InstallRoot 'setup.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
catch { throw 'Outra instalacao do Glimpse esta em andamento. Aguarde e tente novamente.' }
try {
    if (-not (Test-Path -LiteralPath (Join-Path $package 'app-files.json'))) {
        Write-Host 'Preparando o aplicativo a partir do codigo-fonte. Isso pode levar alguns minutos na primeira vez...'
        & (Join-Path $PSScriptRoot 'Setup-Environment.ps1')
        $build = Join-Path $package ('.tools\installer-build-' + [Guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'Build-Package.ps1') -OutputRoot $build -NoZip
        $package = Join-Path $build 'Glimpse-Windows-x64'
    }
    Write-Host 'Verificando todos os arquivos do aplicativo...'
    Test-GlimpsePayload -PackageRoot $package
    $bundledVideo = Join-Path $package 'app\ffmpeg.exe'
    $hasBundledVideo = Test-Path -LiteralPath $bundledVideo
    if (-not $hasBundledVideo) { $ffmpeg = Get-GlimpseFfmpeg -CacheRoot $DependencyCache }
    $version = Join-Path $InstallRoot ('versions\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $version | Out-Null
    Copy-Item -Path (Join-Path $package 'app\*') -Destination $version -Recurse
    if (-not $hasBundledVideo) { Copy-Item -LiteralPath $ffmpeg.Executable -Destination (Join-Path $version 'ffmpeg.exe') }
    $licenses = Join-Path $version 'licenses'
    Copy-Item -LiteralPath (Join-Path $package 'licenses') -Destination $licenses -Recurse
    if (-not $hasBundledVideo) {
        New-Item -ItemType Directory -Force -Path (Join-Path $licenses 'ffmpeg') | Out-Null
        Copy-Item -LiteralPath (Join-Path $ffmpeg.Directory 'LICENSE'),(Join-Path $ffmpeg.Directory 'README.txt') -Destination (Join-Path $licenses 'ffmpeg')
    }
    # Probe the dependency beside the installed executable; no desktop capture occurs.
    $probe = & (Join-Path $version 'ffmpeg.exe') -hide_banner -h encoder=h264_mf 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $probe -notmatch 'Encoder h264_mf') { throw 'O componente instalado nao oferece o codificador necessario.' }
    $verificationInfo = New-Object System.Diagnostics.ProcessStartInfo
    $verificationInfo.FileName = Join-Path $version 'Glimpse.exe'
    $verificationInfo.Arguments = '--verify-installation'
    $verificationInfo.WorkingDirectory = $version
    $verificationInfo.UseShellExecute = $false
    $verificationInfo.CreateNoWindow = $true
    $verification = [System.Diagnostics.Process]::Start($verificationInfo)
    if (-not $verification.WaitForExit(30000)) { $verification.Kill(); throw 'A verificacao do aplicativo demorou mais que o esperado.' }
    if ($verification.ExitCode -ne 0) { throw "O aplicativo nao passou na verificacao. Consulte installation-check.log em $version" }
    $relative = 'versions\' + (Split-Path -Leaf $version) + '\Glimpse.exe'
    $launcher = Join-Path $InstallRoot 'Abrir Glimpse.cmd'
    $temporary = $launcher + '.new'
    @('@echo off','setlocal',('start "Glimpse" /D "%~dp0versions\' + (Split-Path -Leaf $version) + '" "%~dp0' + $relative + '"')) | Set-Content -LiteralPath $temporary -Encoding ASCII
    Move-Item -LiteralPath $temporary -Destination $launcher -Force
    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($folder in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))) {
            $shortcut = $shell.CreateShortcut((Join-Path $folder 'Glimpse.lnk'))
            $shortcut.TargetPath = $launcher; $shortcut.WorkingDirectory = $InstallRoot; $shortcut.IconLocation = (Join-Path $version 'Glimpse.exe') + ',0'; $shortcut.Save()
        }
    }
    $packageVersion=(Get-Content -LiteralPath (Join-Path $package 'app-files.json') -Raw | ConvertFrom-Json).Version
    [ordered]@{ Version=$packageVersion; AppPath=(Join-Path $version 'Glimpse.exe'); InstalledAt=[DateTimeOffset]::Now.ToString('O') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Encoding UTF8
    Write-Host "Glimpse instalado em: $InstallRoot"
    if (-not $NoLaunch) { Start-Process -FilePath (Join-Path $version 'Glimpse.exe') -WorkingDirectory $version -WindowStyle Normal }
}
finally { if ($lock) { $lock.Dispose() } }
