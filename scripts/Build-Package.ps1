param([string]$OutputRoot, [switch]$NoZip, [uri]$RelayUrl)
$ErrorActionPreference = 'Stop'
$relayConfigured = $null -ne $RelayUrl
if ($relayConfigured -and (!$RelayUrl.IsAbsoluteUri -or $RelayUrl.Scheme -ne 'wss' -or $RelayUrl.AbsolutePath -ne '/' -or $RelayUrl.UserInfo -or $RelayUrl.Query -or $RelayUrl.Fragment)) {
    throw 'Informe o endereco WSS do retransmissor, sem caminho ou credenciais.'
}
$ProgressPreference = 'SilentlyContinue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'dist' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$bundle = Join-Path $OutputRoot 'Glimpse-Windows-x64'
if (Test-Path -LiteralPath $bundle) { throw 'A pasta de saida ja existe. Escolha outra pasta para evitar misturar versoes.' }
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($systemDotnet -and ((& $systemDotnet.Source --version) -match '^10\.')) { $dotnet=$systemDotnet.Source }
    else { & (Join-Path $PSScriptRoot 'Setup-Environment.ps1') }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $root '.tools\packages' }
New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'app'),(Join-Path $bundle 'scripts'),(Join-Path $bundle 'licenses') | Out-Null
& $dotnet publish (Join-Path $root 'src\Glimpse\Glimpse.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $bundle 'app')
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel preparar o aplicativo.' }
Copy-Item -LiteralPath (Join-Path $root 'Instalar Glimpse.cmd') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Launch-Portable.cmd') -Destination (Join-Path $bundle 'Abrir Glimpse.cmd')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-App.ps1'),(Join-Path $PSScriptRoot 'Install-Dependencies.ps1') -Destination (Join-Path $bundle 'scripts')
Copy-Item -LiteralPath (Join-Path $root 'docs\installation.md') -Destination (Join-Path $bundle 'LEIA-ME.md')
$assets = Get-Content -LiteralPath (Join-Path $root 'src\Glimpse\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $package = Join-Path $env:NUGET_PACKAGES $library.Name.ToLowerInvariant()
    $destination = Join-Path $bundle ('licenses\' + $library.Name.Replace('/','-'))
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)' -or $_.Extension -eq '.nuspec' } | Copy-Item -Destination $destination
}
. (Join-Path $PSScriptRoot 'Install-Dependencies.ps1')
$video = Get-GlimpseFfmpeg -CacheRoot (Join-Path $root '.tools')
Copy-Item -LiteralPath $video.Executable -Destination (Join-Path $bundle 'app\ffmpeg.exe')
$videoLicenses = Join-Path $bundle 'licenses\ffmpeg'
New-Item -ItemType Directory -Force -Path $videoLicenses | Out-Null
Copy-Item -LiteralPath (Join-Path $video.Directory 'LICENSE'),(Join-Path $video.Directory 'README.txt'),(Join-Path $root 'docs\ffmpeg-provenance.md') -Destination $videoLicenses
$app = Join-Path $bundle 'app'
if ($relayConfigured) { $RelayUrl.AbsoluteUri | Set-Content -LiteralPath (Join-Path $app 'relay-url.txt') -Encoding ASCII }
$files = @(Get-ChildItem -LiteralPath $app -File -Recurse | ForEach-Object { [ordered]@{ Path=$_.FullName.Substring($app.Length+1); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
$projectVersion=([xml](Get-Content -LiteralPath (Join-Path $root 'src\Glimpse\Glimpse.csproj') -Raw)).Project.PropertyGroup.Version
[ordered]@{ Version=$projectVersion; Files=$files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundle 'app-files.json') -Encoding UTF8
Test-GlimpsePayload -PackageRoot $bundle
if (-not $NoZip) {
    $zip = Join-Path $OutputRoot 'Glimpse-Windows-x64.zip'
    Compress-Archive -LiteralPath $bundle -DestinationPath $zip -CompressionLevel Optimal
    (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  Glimpse-Windows-x64.zip' | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
    Write-Host "Pacote pronto: $zip"
}
