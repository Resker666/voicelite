param([Parameter(Mandatory=$true)][string]$PackageRoot, [Parameter(Mandatory=$true)][string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$distRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')) + [IO.Path]::DirectorySeparatorChar
$packageFull = [IO.Path]::GetFullPath($PackageRoot)
$archiveFull = [IO.Path]::GetFullPath($ArchivePath)
if (!$packageFull.StartsWith($distRoot,[StringComparison]::OrdinalIgnoreCase) -or !$archiveFull.StartsWith($distRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($archiveFull) -ne '.zip') { throw 'Package and ZIP must be under this project dist/.' }
if (Test-Path -LiteralPath $archiveFull) { throw 'ZIP already exists; use another name.' }
$manifest = Get-Content -Raw -LiteralPath (Join-Path $packageFull 'package-files.json') | ConvertFrom-Json
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packageFull, $archiveFull, [IO.Compression.CompressionLevel]::Fastest, $true)
$zip = [IO.Compression.ZipFile]::OpenRead($archiveFull)
try {
    $prefix = (Split-Path $packageFull -Leaf) + '/'
    foreach ($entry in $manifest.files) {
        $zipped = $zip.GetEntry($prefix + $entry.path)
        if (!$zipped -or $zipped.Length -ne $entry.bytes) { throw ('ZIP entry missing or wrong size: ' + $entry.path) }
    }
} finally { $zip.Dispose() }
$hash = (Get-FileHash -LiteralPath $archiveFull -Algorithm SHA256).Hash.ToLowerInvariant()
($hash + '  ' + (Split-Path $archiveFull -Leaf)) | Set-Content -LiteralPath ($archiveFull + '.sha256') -Encoding ascii
$report = [PSCustomObject]@{ archive=(Split-Path $archiveFull -Leaf); bytes=(Get-Item -LiteralPath $archiveFull).Length; sha256=$hash; verified_manifest_entries=$manifest.files.Count }
$report | ConvertTo-Json | Set-Content -LiteralPath ($archiveFull + '.metadata.json') -Encoding utf8
$report | ConvertTo-Json
