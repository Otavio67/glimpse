# Installs pinned, project-local dependencies; does not modify the system PATH.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tools = Join-Path $root '.tools'
$dotnetDir = Join-Path $tools 'dotnet'
$ffmpegDir = Join-Path $tools 'ffmpeg-9.0.2-essentials_build'
New-Item -ItemType Directory -Force -Path $tools | Out-Null

$dotnet = Join-Path $dotnetDir 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $installer = Join-Path $tools 'dotnet-install.ps1'
    Write-Host 'Downloading the .NET 10 SDK installer from Microsoft...'
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version '10.0.403' -InstallDir $dotnetDir -NoPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dotnet)) { throw 'The .NET SDK installation failed.' }
}

$ffmpeg = Join-Path $ffmpegDir 'bin\ffmpeg.exe'
if (-not (Test-Path -LiteralPath $ffmpeg)) {
    $archive = Join-Path $tools 'ffmpeg-9.0.2-essentials_build.zip'
    $expected = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
    Write-Host 'Downloading FFmpeg 9.0.2 from Gyan.dev’s GitHub release...'
    Invoke-WebRequest -UseBasicParsing 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' -OutFile $archive
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw 'FFmpeg archive checksum mismatch; installation stopped.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
    if (-not (Test-Path -LiteralPath $ffmpeg)) { throw 'FFmpeg executable not found in the verified archive.' }
}
$env:DOTNET_ROOT = $dotnetDir
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdkVersion = & $dotnet --version
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) { throw 'The project-local .NET 10 SDK is unavailable or incomplete.' }
$runtimes = (& $dotnet --list-runtimes | Out-String)
if ($runtimes -notmatch 'Microsoft.WindowsDesktop.App 10\.') { throw 'The project-local .NET Desktop runtime is missing.' }
Write-Host "SDK: $sdkVersion"
$encoderHelp = (& $ffmpeg -hide_banner -h encoder=h264_mf 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $encoderHelp -notmatch 'Encoder h264_mf') { throw 'Installed FFmpeg does not include the Media Foundation H.264 encoder.' }
$filterHelp = (& $ffmpeg -hide_banner -h bsf=h264_metadata 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $filterHelp -notmatch 'Bit stream filter h264_metadata') { throw 'Installed FFmpeg is missing the H.264 metadata filter.' }
Write-Host "FFmpeg: $ffmpeg"
Write-Host 'Environment ready. Run: powershell -ExecutionPolicy Bypass -File scripts/Dev.ps1 -Action Test'


