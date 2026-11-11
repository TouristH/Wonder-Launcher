#requires -Version 7.4
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
[CmdletBinding()]
param([switch]$PrepareDependencies)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts ('Wonder-minimal-build-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $stage 'publish'
$name = "Wonder-$version-win-x64-minimal"
$executable = Join-Path $artifacts "$name.exe"
$manifestPath = Join-Path $artifacts "$name.dependencies.json"

dotnet publish (Join-Path $root 'src/Wonder.Desktop/Wonder.Desktop.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:WonderExternalDependencies=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
    --artifacts-path (Join-Path $stage 'build') -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Minimal application publish failed.' }

$publishedExecutable = Join-Path $publish 'Wonder.Desktop.exe'
$external = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object FullName -ne $publishedExecutable | Sort-Object FullName)
if ($external.Count -eq 0) { throw 'Expected external third-party dependencies.' }
$files = foreach ($item in $external) {
    $relative = [IO.Path]::GetRelativePath($publish, $item.FullName).Replace('\','/')
    if ($item.Extension -ne '.dll' -or $item.BaseName.StartsWith('Wonder.') -or $relative.Contains('..')) {
        throw "Unexpected external dependency: $relative"
    }
    [ordered]@{ Path=$relative; Bytes=$item.Length; Sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$metadataDirectory = Join-Path $stage 'build/bin/Wonder.Desktop'
$depsFile = @(Get-ChildItem -LiteralPath $metadataDirectory -Recurse -File -Filter 'Wonder.Desktop.deps.json')
$runtimeFile = @(Get-ChildItem -LiteralPath $metadataDirectory -Recurse -File -Filter 'Wonder.Desktop.runtimeconfig.json')
if ($depsFile.Count -ne 1 -or $runtimeFile.Count -ne 1) { throw 'Ambiguous application dependency metadata.' }
$deps = Get-Content -LiteralPath $depsFile[0].FullName -Raw | ConvertFrom-Json
$runtime = Get-Content -LiteralPath $runtimeFile[0].FullName -Raw | ConvertFrom-Json
$packages = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object {
    $parts = $_.Name -split '/',2
    [ordered]@{ Id=$parts[0]; Version=$parts[1]; Url="https://www.nuget.org/packages/$($parts[0])/$($parts[1])" }
})
Copy-Item -LiteralPath $publishedExecutable -Destination $executable -Force
$manifest = [ordered]@{
    SchemaVersion=1; Application="$name.exe"; RuntimeIdentifier='win-x64'
    RequiredDotnet=[ordered]@{ Framework=$runtime.runtimeOptions.framework.name; MinimumVersion=$runtime.runtimeOptions.framework.version; Architecture='x64' }
    DependencyLocation='Place exactly the listed third-party DLLs beside the executable; install .NET separately.'
    Packages=$packages; Files=@($files)
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
$checksums = foreach ($path in @($executable,$manifestPath)) {
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $path -Leaf)
}
[IO.File]::WriteAllText((Join-Path $artifacts "$name-SHA256SUMS"), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
if ($PrepareDependencies) {
    $prepared = Join-Path $artifacts 'Wonder-minimal-dependencies-win-x64'
    foreach ($item in $external) {
        $destination = Join-Path $prepared ([IO.Path]::GetRelativePath($publish,$item.FullName))
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $item.FullName -Destination $destination -Force
    }
    Write-Output "Prepared third-party DLLs: $prepared"
}
Write-Output "Minimal application: $executable ($((Get-Item $executable).Length) bytes)"
Write-Output "External dependency manifest: $manifestPath ($($external.Count) files)"
