#requires -Version 7.4
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/Wonder-plugins' }
$project = Join-Path $root 'src/Wonder.Plugin.PleaseWait'
$manifest = Get-Content -LiteralPath (Join-Path $project 'plugin.json') -Raw | ConvertFrom-Json
dotnet build (Join-Path $project 'Wonder.Plugin.PleaseWait.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Please Wait plugin build failed.' }
$build = Join-Path $project 'bin/Release/net10.0'
$stage = Join-Path $root ('artifacts/Wonder-plugin-staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$files = @('plugin.json','Wonder.Plugin.PleaseWait.dll','README.md','LICENSE','ADDITIONAL_TERMS.md','NOTICE','BRANDING.md')
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination $stage }
$archive = Join-Path $OutputDirectory ($manifest.id + '-' + $manifest.version + '.zip')
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive)
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', $checksum + '  ' + (Split-Path $archive -Leaf) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Plugin package: $archive"
Write-Output "SHA256: $checksum"
