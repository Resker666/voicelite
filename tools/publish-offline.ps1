param(
    [Parameter(Mandatory=$true)][string]$EngineRoot,
    [string]$OutputDirectory,
    [switch]$LocalValidation,
    [switch]$Archive
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot ('dist\VoiceLite-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$outputFull = [IO.Path]::GetFullPath($OutputDirectory)
$projectFull = [IO.Path]::GetFullPath($projectRoot)
$engineFull = [IO.Path]::GetFullPath($EngineRoot)
if (!$outputFull.StartsWith((Join-Path $projectFull 'dist') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be a new directory under this project dist/.' }
if (Test-Path -LiteralPath $outputFull) { throw 'Output already exists. Choose a new directory.' }
$profile = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'packaging\engine-profile.json') | ConvertFrom-Json
if (!$profile.public_redistribution_cleared -and !$LocalValidation) { throw 'Public release blocked: redistribution audit incomplete. -LocalValidation creates a private validation candidate.' }
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $sdk -Parent
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $sdk publish (Join-Path $projectRoot 'src\VoiceLite.Desktop\VoiceLite.Desktop.csproj') -c Release -r win-x64 --self-contained true --disable-build-servers -m:1 -o $outputFull
if ($LASTEXITCODE) { throw 'Desktop publish failed.' }
$resourceArgs = @((Join-Path $PSScriptRoot 'package-resources.py'), '--source', $engineFull, '--destination', (Join-Path $outputFull 'engine'), '--profile', (Join-Path $projectRoot 'packaging\engine-profile.json'))
if ($LocalValidation) { $resourceArgs += '--local-validation' }
& (Join-Path $engineFull 'runtime\python.exe') -B @resourceArgs
if ($LASTEXITCODE) { throw 'Resource staging failed. Incomplete output is retained for diagnosis; use a new output directory on retry.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\START_HERE.txt') -Destination $outputFull
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $outputFull
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $outputFull 'VoiceLite-LICENSE.txt')
$noticeSources = @('microsoft.netcore.app.runtime.win-x64\8.0.33', 'microsoft.windowsdesktop.app.runtime.win-x64\8.0.33', 'communitytoolkit.mvvm\8.4.0')
foreach ($noticeSource in $noticeSources) {
    $packagePath = Join-Path $env:NUGET_PACKAGES $noticeSource
    $noticeTarget = Join-Path (Join-Path $outputFull 'notices') ($noticeSource -replace '\\','-')
    New-Item -ItemType Directory -Path $noticeTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|License|THIRD-PARTY|ThirdParty)' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $noticeTarget }
}
if ($LocalValidation) { 'Private local validation candidate. Not cleared for public redistribution. See THIRD_PARTY_NOTICES.md.' | Set-Content -LiteralPath (Join-Path $outputFull 'LOCAL_VALIDATION_ONLY.txt') }
& (Join-Path $engineFull 'runtime\python.exe') -B (Join-Path $PSScriptRoot 'update-package-manifest.py') $outputFull (Join-Path $projectRoot 'packaging\engine-profile.json')
if ($LASTEXITCODE) { throw 'Package manifest generation failed.' }
if ($Archive) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archivePath = $outputFull + '.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($outputFull, $archivePath, [IO.Compression.CompressionLevel]::Fastest, $true)
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256 | Format-List
}
Write-Output ('Offline candidate: ' + $outputFull)
