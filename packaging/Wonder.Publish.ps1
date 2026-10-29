#requires -Version 7.4
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
[CmdletBinding()]
param([ValidateSet('all', 'linux-x64', 'win-x64', 'osx-x64', 'osx-arm64')][string]$Runtime = 'all')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Formats.Tar
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$artifactRoot = Join-Path $root 'artifacts'
$stage = Join-Path $artifactRoot ('Wonder-package-staging-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $stage 'source'
$runtimes = if ($Runtime -eq 'all') { @('win-x64','linux-x64','osx-x64','osx-arm64') } else { @($Runtime) }
$sourceArchive = Join-Path $artifactRoot "Wonder-$version-source.tar.gz"
$documents = @('README.md','Wonder.md','PRIVACY.md','LICENSE','LICENSE.zh-CN.md',
    'ADDITIONAL_TERMS.md','BRANDING.md','NOTICE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md')
$guides = @('README.md','13-wonder-ui-foundation.md','14-settings-component.md',
    '15-portable-data-and-motion.md','16-component-system.md','18-instance-management-and-launch.md',
    '21-settings-pages-and-instance-cards.md','22-instance-selection-and-directory-actions.md',
    '26-account-management.md','28-login-diagnostics-and-default-skins.md',
    '30-layered-skins-capes-and-avatars.md','31-linux-trial.md','32-native-cross-platform.md')

function Copy-ReleaseDocs([string]$destination) {
    New-Item -ItemType Directory -Path (Join-Path $destination 'Guides') -Force | Out-Null
    foreach ($name in $documents) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $destination }
    foreach ($name in $guides) { Copy-Item -LiteralPath (Join-Path $root "Guides/$name") -Destination (Join-Path $destination 'Guides') }
    Copy-Item -LiteralPath (Join-Path $root 'ThirdPartyLicenses') -Destination $destination -Recurse
}

function Write-TarGzip([string]$directory, [string]$target, [string]$prefix) {
    $file = [IO.File]::Create($target)
    $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionLevel]::Optimal)
    $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
    try {
        foreach ($item in (Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object FullName)) {
            $relative = [IO.Path]::GetRelativePath($directory, $item.FullName).Replace('\','/')
            if ($relative -match '(^|/)(WonderData|bin|obj|\.git|tests)(/|$)') { throw "Private/generated path in archive: $relative" }
            $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, "$prefix/$relative")
            $entry.Mode = [IO.UnixFileMode]$(if ($relative -eq 'Wonder.Desktop' -or $relative.EndsWith('/MacOS/Wonder.Desktop') -or $relative.EndsWith('.sh')) { 493 } else { 420 })
            $entry.Uid = 0; $entry.Gid = 0; $entry.UserName = ''; $entry.GroupName = ''
            $entry.ModificationTime = [DateTimeOffset]$item.LastWriteTimeUtc
            $inputFile = [IO.File]::OpenRead($item.FullName)
            try { $entry.DataStream = $inputFile; $writer.WriteEntry($entry) } finally { $inputFile.Dispose() }
        }
    } finally { $writer.Dispose(); $gzip.Dispose(); $file.Dispose() }
}

Push-Location $root
try {
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    # An explicit source allowlist includes current local changes and new files, never user data.
    Copy-ReleaseDocs $source
    foreach ($name in @('Wonder.slnx','Directory.Build.props','.gitignore')) {
        Copy-Item -LiteralPath (Join-Path $root $name) -Destination $source
    }
    foreach ($folder in @('src','packaging')) {
        foreach ($item in (Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File)) {
            $relative = [IO.Path]::GetRelativePath($root, $item.FullName)
            if ($relative -match '(^|[\\/])(bin|obj|WonderData)([\\/]|$)') { continue }
            if ($item.Extension -notin @('.cs','.csproj','.manifest','.axaml','.props','.targets','.ps1','.sh','.plist')) { throw "Review new source file type: $relative" }
            $destination = Join-Path $source $relative
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $item.FullName -Destination $destination
        }
    }
    # All native builds consume the exact same source snapshot that we distribute.
    Write-TarGzip $source $sourceArchive "Wonder-$version-source"
    dotnet build Wonder.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    $archives = @($sourceArchive)
    foreach ($targetRuntime in $runtimes) {
        $app = Join-Path $stage "Wonder-$targetRuntime"
        $output = Join-Path $artifactRoot "Wonder-$targetRuntime"
        $macOS = $targetRuntime.StartsWith('osx-')
        $publish = if ($macOS) { Join-Path $app 'Wonder Launcher.app/Contents/MacOS' } else { $app }
        New-Item -ItemType Directory -Path $publish -Force | Out-Null
        dotnet publish (Join-Path $source 'src/Wonder.Desktop/Wonder.Desktop.csproj') -c Release -r $targetRuntime --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
            -p:DebugType=None -p:DebugSymbols=false -o $publish --nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $targetRuntime" }
        foreach ($name in $documents) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $app }
        foreach ($folder in @('Guides','ThirdPartyLicenses')) { Copy-Item -LiteralPath (Join-Path $source $folder) -Destination $app -Recurse }
        if ($targetRuntime -eq 'linux-x64') {
            $script = [IO.File]::ReadAllText((Join-Path $source 'packaging/start-Wonder.sh')).Replace("`r`n", "`n")
            [IO.File]::WriteAllText((Join-Path $app 'start-Wonder.sh'), $script, [Text.UTF8Encoding]::new($false))
        }
        if ($macOS) {
            $plist = [IO.File]::ReadAllText((Join-Path $source 'packaging/Wonder.Info.plist')).Replace('@VERSION@',$version)
            [IO.File]::WriteAllText((Join-Path $app 'Wonder Launcher.app/Contents/Info.plist'), $plist, [Text.UTF8Encoding]::new($false))
            Copy-Item -LiteralPath (Join-Path $source 'packaging/Wonder.macOS.entitlements.plist') -Destination $app
            $script = [IO.File]::ReadAllText((Join-Path $source 'packaging/sign-Wonder-macos.sh')).Replace("`r`n", "`n")
            [IO.File]::WriteAllText((Join-Path $app 'sign-Wonder-macos.sh'), $script, [Text.UTF8Encoding]::new($false))
            if ($IsMacOS) { & /bin/sh (Join-Path $app 'sign-Wonder-macos.sh'); if ($LASTEXITCODE -ne 0) { throw 'Local ad hoc signing failed.' } }
        }
        if (!$IsWindows) {
            foreach ($entry in (Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object { $_.Name -eq 'Wonder.Desktop' -or $_.Extension -eq '.sh' })) {
                [IO.File]::SetUnixFileMode($entry.FullName, [IO.UnixFileMode]493)
            }
        }
        $label = if ($targetRuntime -eq 'win-x64') { $targetRuntime } else { "$targetRuntime-preview" }
        $archive = Join-Path $artifactRoot "Wonder-$version-$label.tar.gz"
        Write-TarGzip $app $archive "Wonder-$targetRuntime"
        $archives += $archive
        # Copy generated files only; keep existing WonderData and running Windows sessions.
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        foreach ($item in (Get-ChildItem -LiteralPath $app)) {
            $destination = Join-Path $output $item.Name
            $previousExecutable = $null
            if ($item.Name -eq 'Wonder.Desktop.exe' -and (Test-Path -LiteralPath $destination)) {
                $previousExecutable = Join-Path $artifactRoot ('Wonder-previous-' + [guid]::NewGuid().ToString('N') + '.exe')
                Move-Item -LiteralPath $destination -Destination $previousExecutable
            }
            try { Copy-Item -LiteralPath $item.FullName -Destination $output -Recurse -Force }
            catch {
                if ($previousExecutable -and !(Test-Path -LiteralPath $destination)) { Move-Item -LiteralPath $previousExecutable -Destination $destination }
                throw
            }
        }
        Write-Output "Application: $output"
        Write-Output "Package: $archive"
    }
    $checksums = foreach ($path in $archives) {
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $path -Leaf)
    }
    [IO.File]::WriteAllText((Join-Path $artifactRoot "Wonder-$version-SHA256SUMS"), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Corresponding source: $sourceArchive"
    Write-Output "Checksums: $($checksums -join '; ')"
} finally { Pop-Location }
