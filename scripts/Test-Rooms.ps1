$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
foreach ($project in @('Glimpse.Rooms', 'Glimpse.InternetRooms', 'Glimpse.NativeRooms')) {
    & $dotnet run --project (Join-Path $root "tests\$project\$project.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
