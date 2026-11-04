param([int]$DurationSeconds = 120)
$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$ffmpeg = Join-Path $root '.tools\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe'
$env:PATH = (Split-Path -Parent $ffmpeg) + ';' + $env:PATH
$project = Join-Path $root 'benchmarks\Glimpse.Baseline\Glimpse.Baseline.csproj'
& $dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
$dll = Join-Path $root 'benchmarks\Glimpse.Baseline\bin\Release\net10.0-windows10.0.19041.0\Glimpse.Baseline.dll'
$folder=Join-Path $root 'docs\perf'
New-Item -ItemType Directory -Force $folder | Out-Null
$csv=Join-Path $folder 'baseline-synthetic-720p30.csv'
$log=Join-Path $folder 'baseline-synthetic-720p30.log'
$err=Join-Path $folder 'baseline-synthetic-720p30-error.log'
'seconds,glimpse_cpu_pct_total,ffmpeg_cpu_pct_total,glimpse_working_mb,ffmpeg_working_mb,gpu_total_pct,gpu_encoder_pct,gpu_memory_mb' | Set-Content -Encoding UTF8 $csv
$bench = Start-Process -FilePath $dotnet -ArgumentList @('"'+$dll+'"',"$DurationSeconds") -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
$benchPid = $bench.Id
$timer = [Diagnostics.Stopwatch]::StartNew(); $previousTime=0.0; $previousCpu=0.0; $previousFf=0.0; $previousChild=0
while ($timer.Elapsed.TotalSeconds -lt ($DurationSeconds + 35)) {
    Start-Sleep -Milliseconds 1000
    $bench.Refresh(); if ($bench.HasExited) { break }
    $elapsed=$timer.Elapsed.TotalSeconds; $delta=$elapsed-$previousTime
    $hostCpu=$bench.TotalProcessorTime.TotalSeconds
    $hostPct=if($previousTime -gt 0){100*($hostCpu-$previousCpu)/$delta/[Environment]::ProcessorCount}else{0}
    $childInfo = Get-CimInstance Win32_Process -Filter "ParentProcessId=$benchPid" | Where-Object Name -EQ 'ffmpeg.exe' | Select-Object -First 1
    $ffPct=0; $ffMem=0
    if ($childInfo) {
        $child=Get-Process -Id $childInfo.ProcessId -ErrorAction SilentlyContinue
        if ($child) { $childCpu=$child.TotalProcessorTime.TotalSeconds; $ffMem=$child.WorkingSet64/1MB; if ($previousTime -gt 0 -and $previousChild -eq $child.Id) {$ffPct=100*($childCpu-$previousFf)/$delta/[Environment]::ProcessorCount}; $previousFf=$childCpu; $previousChild=$child.Id }
    }
    $gpu=@('','',''); $tool=Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($tool) { $raw=(& $tool.Source '--query-gpu=utilization.gpu,utilization.encoder,memory.used' '--format=csv,noheader,nounits' 2>$null | Select-Object -First 1); if($raw){$gpu=$raw.Split(',').Trim()} }
    $line='{0:F1},{1:F3},{2:F3},{3:F1},{4:F1},{5},{6},{7}' -f $elapsed,$hostPct,$ffPct,($bench.WorkingSet64/1MB),$ffMem,$gpu[0],$gpu[1],$gpu[2]
    Add-Content -LiteralPath $csv -Value $line
    $previousTime=$elapsed; $previousCpu=$hostCpu
}
if (-not $bench.WaitForExit(5000)) { throw "O processo de benchmark não encerrou no tempo esperado." }
if (($null -ne $bench.ExitCode -and $bench.ExitCode -ne 0) -or -not (Select-String -Path $log -SimpleMatch "error=none" -Quiet)) { Get-Content $err; throw "Benchmark did not complete successfully; exit code: $($bench.ExitCode)." }
Get-Content $log
Write-Output "Samples saved to $csv"
