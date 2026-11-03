param(
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$ReferencePath,
    [Parameter(Mandatory=$true)][string]$DataRoot
)
$ErrorActionPreference = 'Stop'
$packageFull = [IO.Path]::GetFullPath($PackageRoot)
$dataFull = [IO.Path]::GetFullPath($DataRoot)
if (Test-Path -LiteralPath $dataFull) { throw 'Validation requires a fresh data/cache directory.' }
New-Item -ItemType Directory -Path $dataFull | Out-Null
$info = [Diagnostics.ProcessStartInfo]::new((Join-Path $packageFull 'VoiceLite.exe'))
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WorkingDirectory = $dataFull
foreach ($arg in @('--smoke-ui', '--reference', [IO.Path]::GetFullPath($ReferencePath), '--data-dir', $dataFull)) { $info.ArgumentList.Add($arg) }
foreach ($name in @('DOTNET_ROOT','DOTNET_ROOT_X64','PYTHONPATH','PYTHONHOME')) { $info.Environment.Remove($name) | Out-Null }
$info.Environment['PATH'] = [Environment]::SystemDirectory + ';' + [Environment]::GetFolderPath('Windows')
$info.Environment['HF_HOME'] = Join-Path $dataFull 'empty-hf-cache'
$info.Environment['TORCH_HOME'] = Join-Path $dataFull 'empty-torch-cache'
$info.Environment['XDG_CACHE_HOME'] = Join-Path $dataFull 'empty-xdg-cache'
$info.Environment['HF_HUB_OFFLINE'] = '1'
$info.Environment['TRANSFORMERS_OFFLINE'] = '1'
$process = [Diagnostics.Process]::Start($info)
$clock = [Diagnostics.Stopwatch]::StartNew()
$samples = [Collections.Generic.List[int]]::new()
try {
    while (!$process.WaitForExit(1000)) {
        if ($clock.Elapsed.TotalSeconds -gt 240) { throw 'Offline UI validation exceeded 240 seconds.' }
        $gpuSample = & (Join-Path ([Environment]::SystemDirectory) 'nvidia-smi.exe') --query-gpu=memory.used --format=csv,noheader,nounits
        if ($LASTEXITCODE -eq 0 -and $gpuSample -match '^\d+$') { $samples.Add([int]$gpuSample) }
    }
    $reportPath = Join-Path $dataFull 'ui-smoke.json'
    if (!(Test-Path -LiteralPath $reportPath)) { throw ('No UI report; exit=' + $process.ExitCode) }
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    $report | Add-Member -NotePropertyName sampled_board_memory_peak_mib -NotePropertyValue (($samples | Measure-Object -Maximum).Maximum)
    $report | Add-Member -NotePropertyName memory_sampling_note -NotePropertyValue '1-second board-wide samples; includes other applications, not an exact per-process peak.'
    $report | Add-Member -NotePropertyName clean_machine -NotePropertyValue $false
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Get-Content -LiteralPath $reportPath
    if ($process.ExitCode -ne 0 -or !$report.passed) { throw 'Offline candidate UI validation failed.' }
} finally {
    if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
