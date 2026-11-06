param([ValidateSet('Build','Test','Run')][string]$Action = 'Build')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnetDir = Join-Path $root '.tools\dotnet'
$dotnet = Join-Path $dotnetDir 'dotnet.exe'
$ffmpeg = Join-Path $root '.tools\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'
if (-not (Test-Path -LiteralPath $dotnet) -or -not (Test-Path -LiteralPath $ffmpeg)) {
    throw 'Project-local dependencies are missing. Run scripts/Setup-Environment.ps1 first.'
}
$env:DOTNET_ROOT = $dotnetDir
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PATH = (Split-Path -Parent $ffmpeg) + ';' + $dotnetDir + ';' + $env:PATH
$env:GLIMPSE_FFMPEG = $ffmpeg
$appProject = Join-Path $root 'src\Glimpse\Glimpse.csproj'
$testProject = Join-Path $root 'tests\Glimpse.Headless\Glimpse.Headless.csproj'
$twoProcessProject = Join-Path $root 'tests\Glimpse.TwoProcess\Glimpse.TwoProcess.csproj'
switch ($Action) {
    'Build' { & $dotnet build $appProject -c Release }
    'Test'  { & (Join-Path $PSScriptRoot 'Test-Rooms.ps1'); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; & $dotnet run --project $testProject -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; & $dotnet run --project $twoProcessProject -c Release }
    'Run'   { & $dotnet run --project $appProject -c Release }
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }


