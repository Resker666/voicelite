$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json'
$pinnedSdk = (Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'global.json') | ConvertFrom-Json).sdk.version
$release = $metadata.releases | Where-Object { $_.sdk.version -eq $pinnedSdk } | Select-Object -First 1
$asset = $release.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (!$asset -or !($asset.url -match '^https://(builds\.dotnet\.microsoft\.com|download\.visualstudio\.microsoft\.com)/')) { throw 'SDK source is not an expected Microsoft origin.' }
$sdkRoot = Join-Path $toolRoot 'dotnet'
$archive = Join-Path $toolRoot ('dotnet-sdk-' + $release.sdk.version + '-win-x64.zip')
if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest $asset.url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $asset.hash) { throw 'SDK SHA-512 mismatch.' }
New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $sdkRoot ('sdk\' + $pinnedSdk)))) { Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force }
[PSCustomObject]@{ Version=$release.sdk.version; Url=$asset.url; Sha512=$asset.hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $toolRoot 'sdk-source.json') -Encoding utf8
& (Join-Path $sdkRoot 'dotnet.exe') --info
if ($LASTEXITCODE) { throw 'SDK did not start.' }
