param(
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$ReferencePath,
    [Parameter(Mandatory=$true)][string]$DataRoot,
    [string]$EngineRoot,
    [string[]]$AuxiliaryReferencePaths = @(),
    [ValidateSet('auto','nvidia','cpu')][string]$Device = 'auto',
    [ValidateRange(10,3600)][int]$TimeoutSeconds = 1000
)
$ErrorActionPreference = 'Stop'

function Get-VoiceLiteMemorySample([int]$RootProcessId) {
    $processes = @(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,Name,WorkingSetSize -OperationTimeoutSec 2 -ErrorAction Stop)
    $descendants = [Collections.Generic.HashSet[uint32]]::new()
    [void]$descendants.Add([uint32]$RootProcessId)
    do {
        $added = $false
        foreach ($entry in $processes) {
            if ($descendants.Contains([uint32]$entry.ParentProcessId) -and $descendants.Add([uint32]$entry.ProcessId)) { $added = $true }
        }
    } while ($added)
    [long]$treeBytes = 0
    [long]$pythonBytes = 0
    foreach ($entry in $processes) {
        if (!$descendants.Contains([uint32]$entry.ProcessId)) { continue }
        $treeBytes += [long]$entry.WorkingSetSize
        if ($entry.Name -in @('python.exe','pythonw.exe')) { $pythonBytes += [long]$entry.WorkingSetSize }
    }
    [pscustomobject]@{ TreeBytes = $treeBytes; PythonBytes = $pythonBytes }
}

function Get-VoiceLiteGpuSample([string]$ToolPath) {
    $samplingInfo = [Diagnostics.ProcessStartInfo]::new($ToolPath)
    $samplingInfo.UseShellExecute = $false
    $samplingInfo.CreateNoWindow = $true
    $samplingInfo.RedirectStandardOutput = $true
    $samplingInfo.RedirectStandardError = $true
    foreach ($argument in @('--query-gpu=memory.used','--format=csv,noheader,nounits')) { $samplingInfo.ArgumentList.Add($argument) }
    $samplingProcess = [Diagnostics.Process]::Start($samplingInfo)
    try {
        $output = $samplingProcess.StandardOutput.ReadToEndAsync()
        $errors = $samplingProcess.StandardError.ReadToEndAsync()
        if (!$samplingProcess.WaitForExit(2000)) { return $null }
        [void]$errors.GetAwaiter().GetResult()
        $lines = @($output.GetAwaiter().GetResult().Split([char]10) | ForEach-Object { $_.Trim() } | Where-Object { $_.Length })
        if ($samplingProcess.ExitCode -ne 0 -or !$lines.Count -or @($lines | Where-Object { $_ -notmatch '^\d+$' }).Count) { return $null }
        [int](($lines | ForEach-Object { [int]$_ } | Measure-Object -Sum).Sum)
    } finally {
        try {
            if (!$samplingProcess.HasExited) {
                $samplingProcess.Kill($true)
                [void]$samplingProcess.WaitForExit(5000)
            }
        } finally { $samplingProcess.Dispose() }
    }
}

$packageFull = [IO.Path]::GetFullPath($PackageRoot)
$dataFull = [IO.Path]::GetFullPath($DataRoot)
if (Test-Path -LiteralPath $dataFull) { throw 'Validation requires a fresh data/cache directory.' }
New-Item -ItemType Directory -Path $dataFull | Out-Null
$info = [Diagnostics.ProcessStartInfo]::new((Join-Path $packageFull 'VoiceLite.exe'))
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WorkingDirectory = $dataFull
foreach ($arg in @('--smoke-ui', '--reference', [IO.Path]::GetFullPath($ReferencePath), '--data-dir', $dataFull, '--device', $Device)) { $info.ArgumentList.Add($arg) }
if ($EngineRoot) { $info.ArgumentList.Add('--engine'); $info.ArgumentList.Add([IO.Path]::GetFullPath($EngineRoot)) }
foreach ($auxiliaryReference in $AuxiliaryReferencePaths) {
    $info.ArgumentList.Add('--aux-reference'); $info.ArgumentList.Add([IO.Path]::GetFullPath($auxiliaryReference))
}
foreach ($name in @('DOTNET_ROOT','DOTNET_ROOT_X64','PYTHONPATH','PYTHONHOME')) { $info.Environment.Remove($name) | Out-Null }
$info.Environment['PATH'] = [Environment]::SystemDirectory + ';' + [Environment]::GetFolderPath('Windows')
$info.Environment['HF_HOME'] = Join-Path $dataFull 'empty-hf-cache'
$info.Environment['TORCH_HOME'] = Join-Path $dataFull 'empty-torch-cache'
$info.Environment['XDG_CACHE_HOME'] = Join-Path $dataFull 'empty-xdg-cache'
$info.Environment['HF_HUB_OFFLINE'] = '1'
$info.Environment['TRANSFORMERS_OFFLINE'] = '1'
$process = [Diagnostics.Process]::Start($info)
$clock = [Diagnostics.Stopwatch]::StartNew()
$gpuTool = Join-Path ([Environment]::SystemDirectory) 'nvidia-smi.exe'
$sampleGpu = $Device -ne 'cpu' -and (Test-Path -LiteralPath $gpuTool)
$gpuSamples = [Collections.Generic.List[int]]::new()
$treeSamples = [Collections.Generic.List[long]]::new()
$pythonSamples = [Collections.Generic.List[long]]::new()
$memorySamplingError = $null
try {
    while (!$process.WaitForExit(1000)) {
        if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) { throw ('Offline UI validation exceeded ' + $TimeoutSeconds + ' seconds.') }
        $systemMemory = Get-CimInstance Win32_OperatingSystem -Property FreeVirtualMemory -OperationTimeoutSec 2
        if ([long]$systemMemory.FreeVirtualMemory -lt 524288) { throw 'Validation stopped because system commit headroom fell below 512 MiB.' }
        if ($null -eq $memorySamplingError) {
            try {
                $memory = Get-VoiceLiteMemorySample $process.Id
                $treeSamples.Add($memory.TreeBytes)
                $pythonSamples.Add($memory.PythonBytes)
            } catch { $memorySamplingError = $_.Exception.GetType().Name }
        }
        if ($sampleGpu) {
            try {
                $gpuSample = Get-VoiceLiteGpuSample $gpuTool
                if ($null -eq $gpuSample) { $sampleGpu = $false } else { $gpuSamples.Add($gpuSample) }
            } catch { $sampleGpu = $false }
        }
    }
    $reportPath = Join-Path $dataFull 'ui-smoke.json'
    if (!(Test-Path -LiteralPath $reportPath)) { throw ('No UI report; exit=' + $process.ExitCode) }
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    $treePeak = if ($treeSamples.Count) { [Math]::Round(($treeSamples | Measure-Object -Maximum).Maximum / 1MB, 2) } else { $null }
    $pythonPeak = if ($pythonSamples.Count) { [Math]::Round(($pythonSamples | Measure-Object -Maximum).Maximum / 1MB, 2) } else { $null }
    $report | Add-Member -Force -NotePropertyName requested_device -NotePropertyValue $Device
    $report | Add-Member -Force -NotePropertyName sampled_process_tree_working_set_peak_mib -NotePropertyValue $treePeak
    $report | Add-Member -Force -NotePropertyName sampled_python_working_set_peak_mib -NotePropertyValue $pythonPeak
    $report | Add-Member -Force -NotePropertyName process_memory_sample_count -NotePropertyValue $treeSamples.Count
    $report | Add-Member -Force -NotePropertyName process_memory_sampling_error -NotePropertyValue $memorySamplingError
    $report | Add-Member -Force -NotePropertyName sampled_board_memory_peak_mib -NotePropertyValue (($gpuSamples | Measure-Object -Maximum).Maximum)
    $report | Add-Member -Force -NotePropertyName memory_sampling_note -NotePropertyValue 'Approximately 1-second process-tree and Python working-set samples, not exact peaks; summed working sets may count shared pages more than once. GPU samples sum all boards and include other applications. CPU mode does not invoke the GPU sampling tool.'
    $report | Add-Member -Force -NotePropertyName clean_machine -NotePropertyValue $false
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Get-Content -LiteralPath $reportPath
    if ($process.ExitCode -ne 0 -or !$report.passed) { throw 'Offline candidate UI validation failed.' }
} catch {
    [pscustomobject]@{
        passed = $false
        requested_device = $Device
        error = $_.Exception.Message
        elapsed_seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
        sampled_process_tree_working_set_peak_mib = if ($treeSamples.Count) { [Math]::Round(($treeSamples | Measure-Object -Maximum).Maximum / 1MB, 2) } else { $null }
        sampled_python_working_set_peak_mib = if ($pythonSamples.Count) { [Math]::Round(($pythonSamples | Measure-Object -Maximum).Maximum / 1MB, 2) } else { $null }
        memory_sampling_error = $memorySamplingError
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataFull 'validation-failure.json') -Encoding utf8
    throw
} finally {
    try {
        if (!$process.HasExited) {
            $process.Kill($true)
            if (!$process.WaitForExit(10000)) { throw 'Validation process did not exit after termination.' }
        }
    } finally { $process.Dispose() }
}
