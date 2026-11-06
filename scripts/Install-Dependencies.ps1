# Shared by source and release installers. Downloads stay inside the chosen cache.
function Get-GlimpseFfmpeg {
    param([Parameter(Mandatory=$true)][string]$CacheRoot)
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $cache = [IO.Path]::GetFullPath($CacheRoot)
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $directory = Join-Path $cache 'ffmpeg-9.0.2-essentials_build'
    $executable = Join-Path $directory 'bin\ffmpeg.exe'
    $exeHash = '3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec'
    if (-not (Test-Path -LiteralPath $executable) -or (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() -ne $exeHash) {
        $archive = Join-Path $cache 'ffmpeg-9.0.2-essentials_build.zip'
        $archiveHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
        if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveHash) {
            Write-Host 'Baixando o componente de video (FFmpeg)...'
            $partial = $archive + '.partial'
            Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' -OutFile $partial
            if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveHash) { throw 'O arquivo de video falhou na verificacao. Tente instalar novamente.' }
            Move-Item -LiteralPath $partial -Destination $archive -Force
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $cache -Force
    }
    if (-not (Test-Path -LiteralPath $executable) -or (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() -ne $exeHash) { throw 'O componente de video esta incompleto ou danificado.' }
    return [pscustomobject]@{ Directory=$directory; Executable=$executable; Hash=$exeHash }
}

function Test-GlimpsePayload {
    param([Parameter(Mandatory=$true)][string]$PackageRoot)
    $app = [IO.Path]::GetFullPath((Join-Path $PackageRoot 'app'))
    $manifest = Get-Content -LiteralPath (Join-Path $PackageRoot 'app-files.json') -Raw | ConvertFrom-Json
    if (-not $manifest.Files -or $manifest.Files.Count -lt 2) { throw 'O pacote do aplicativo esta incompleto.' }
    foreach ($entry in $manifest.Files) {
        $file = [IO.Path]::GetFullPath((Join-Path $app $entry.Path))
        if (-not $file.StartsWith($app + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'O pacote contem um caminho invalido.' }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Sha256) { throw "Arquivo incompleto ou danificado: $($entry.Path). Baixe o pacote novamente." }
    }
    foreach ($required in @('Glimpse.exe','Glimpse.dll','coreclr.dll','PresentationFramework.dll')) { if (-not (Test-Path -LiteralPath (Join-Path $app $required))) { throw "Arquivo necessario ausente: $required" } }
}
