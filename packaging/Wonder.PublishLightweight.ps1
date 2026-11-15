#requires -Version 7.4
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
[CmdletBinding()]
param(
    [ValidateSet('win-x64','linux-x64')][string[]]$Runtime = @('win-x64','linux-x64'),
    [string]$SourceRoot,
    [string]$SourceArchive,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Formats.Tar
$repository = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $OutputDirectory ('Wonder-lightweight-staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if ($SourceRoot -and $SourceArchive) { throw 'Select SourceRoot or SourceArchive, not both.' }
if (!$SourceRoot) {
    if (!$SourceArchive) { $SourceArchive = Join-Path $OutputDirectory "Wonder-$version-source.tar.gz" }
    if (!(Test-Path -LiteralPath $SourceArchive -PathType Leaf)) { throw 'First run Wonder.Publish.ps1 -IncludeLightweight, or provide its matching source archive.' }
    $extract = Join-Path $stage 'source'
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    $file = [IO.File]::OpenRead($SourceArchive)
    $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress)
    try { [System.Formats.Tar.TarFile]::ExtractToDirectory($gzip, $extract, $false) }
    finally { $gzip.Dispose(); $file.Dispose() }
    $roots = @(Get-ChildItem -LiteralPath $extract -Directory)
    if ($roots.Count -ne 1) { throw 'The source archive must contain one Wonder source directory.' }
    $SourceRoot = $roots[0].FullName
}
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$version = ([xml](Get-Content -LiteralPath (Join-Path $SourceRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$documents = @('README.md','Wonder.md','PRIVACY.md','LICENSE','LICENSE.zh-CN.md','ADDITIONAL_TERMS.md','BRANDING.md','NOTICE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md')
$guides = @('README.md','13-wonder-ui-foundation.md','14-settings-component.md','15-portable-data-and-motion.md','16-component-system.md',
    '18-instance-management-and-launch.md','21-settings-pages-and-instance-cards.md','22-instance-selection-and-directory-actions.md',
    '26-account-management.md','28-login-diagnostics-and-default-skins.md','30-layered-skins-capes-and-avatars.md','31-linux-trial.md',
    '32-native-cross-platform.md','33-download-center.md','35-plugins.md','36-plugin-development.md','37-lightweight-distribution.md')
$archives = @()
foreach ($rid in $Runtime | Select-Object -Unique) {
    $name = "Wonder-$rid-lightweight"
    $app = Join-Path $stage $name
    $executable = if ($rid -eq 'win-x64') { 'Wonder.Desktop.exe' } else { 'Wonder.Desktop' }
    # All app dependencies remain bundled. Only Microsoft.NETCore.App is external.
    dotnet publish (Join-Path $SourceRoot 'src/Wonder.Desktop/Wonder.Desktop.csproj') -c Release -r $rid --self-contained false `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:WonderExternalDependencies=false `
        -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
        --artifacts-path (Join-Path $stage "build-$rid") -o $app --nologo
    if ($LASTEXITCODE -ne 0) { throw "Lightweight publish failed: $rid" }
    $files = @(Get-ChildItem -LiteralPath $app -Recurse -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne $executable) { throw "Expected one complete framework-dependent payload for $rid; review dependencies." }
    $scripts = if ($rid -eq 'win-x64') { @('Wonder.cmd','Wonder.Runtime.ps1') } else { @('Wonder.sh','Wonder.Runtime.sh') }
    foreach ($script in $scripts) {
        $text = [IO.File]::ReadAllText((Join-Path $SourceRoot "packaging/$script"))
        if ($script.EndsWith('.sh')) { $text = $text.Replace("`r`n", "`n") }
        if ($script.EndsWith('.cmd')) { $text = $text.Replace("`r`n", "`n").Replace("`n", "`r`n") }
        [IO.File]::WriteAllText((Join-Path $app $script), $text, [Text.UTF8Encoding]::new($script.EndsWith('.ps1')))
    }
    foreach ($document in $documents) { Copy-Item -LiteralPath (Join-Path $SourceRoot $document) -Destination $app }
    $help = Join-Path $app 'Guides'
    New-Item -ItemType Directory -Path $help -Force | Out-Null
    foreach ($guide in $guides) { Copy-Item -LiteralPath (Join-Path $SourceRoot "Guides/$guide") -Destination $help }
    Copy-Item -LiteralPath (Join-Path $SourceRoot 'ThirdPartyLicenses') -Destination $app -Recurse
    if ($rid -eq 'win-x64') {
        $archive = Join-Path $OutputDirectory "Wonder-$version-$rid-lightweight.zip"
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        [IO.Compression.ZipFile]::CreateFromDirectory($app, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
    } else {
        $archive = Join-Path $OutputDirectory "Wonder-$version-$rid-lightweight.tar.gz"
        $output = [IO.File]::Create($archive)
        $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::Optimal)
        $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
        try {
            foreach ($item in Get-ChildItem -LiteralPath $app -Recurse -File | Sort-Object FullName) {
                $relative = [IO.Path]::GetRelativePath($app, $item.FullName).Replace('\','/')
                $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, "$name/$relative")
                $entry.Mode = [IO.UnixFileMode]$(if ($relative -eq 'Wonder.Desktop' -or $relative.EndsWith('.sh')) { 493 } else { 420 })
                $entry.Uid = 0; $entry.Gid = 0; $entry.UserName = ''; $entry.GroupName = ''
                $entry.ModificationTime = [DateTimeOffset]$item.LastWriteTimeUtc
                $input = [IO.File]::OpenRead($item.FullName)
                try { $entry.DataStream = $input; $writer.WriteEntry($entry) } finally { $input.Dispose() }
            }
        } finally { $writer.Dispose(); $gzip.Dispose(); $output.Dispose() }
    }
    $archives += $archive
    Write-Output "Lightweight package: $archive ($((Get-Item -LiteralPath $archive).Length) bytes)"
}
$sums = foreach ($archive in $archives) { (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $archive -Leaf) }
[IO.File]::WriteAllText((Join-Path $OutputDirectory "Wonder-$version-lightweight-SHA256SUMS"), ($sums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
