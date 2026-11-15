#requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
# Dot-source this file to test the functions against explicit, synthetic directories.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$script:WonderRuntimeVersion = '10.0.15'
$script:WonderRuntimeUrl = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.15/dotnet-runtime-10.0.15-win-x64.zip'
$script:WonderRuntimeSha512 = '06f0bedd59ae4ac722ada805a6d6bbf605bcdc790c71f59b7003878072902b5cde5dec8c40c0baa81192660d4748e9d79afdbdcb0c0ea9d6b821d0a044c4c6dd'

function Test-WonderRuntime([string]$Directory) {
    if ([string]::IsNullOrWhiteSpace($Directory)) { return $false }
    $hostPath = Join-Path $Directory 'dotnet.exe'
    if (!(Test-Path -LiteralPath $hostPath -PathType Leaf)) { return $false }
    $reader = $null
    try {
        # Do not mistake an x86 or arm64 host on PATH for the required x64 runtime.
        $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($hostPath))
        if ($reader.ReadUInt16() -ne 0x5a4d) { return $false }
        $reader.BaseStream.Position = 0x3c
        $reader.BaseStream.Position = $reader.ReadInt32()
        if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) { return $false }
        $reader.Dispose(); $reader = $null
        $runtimes = @(& $hostPath --list-runtimes 2>$null)
        return $LASTEXITCODE -eq 0 -and @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.0\.[0-9]+ \[' }).Count -gt 0
    } catch { return $false }
    finally { if ($null -ne $reader) { $reader.Dispose() } }
}

function Get-WonderRuntimeCandidates {
    # Registered/default system locations take precedence over private installations.
    $key = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64' -ErrorAction SilentlyContinue
    if ($null -ne $key -and $key.PSObject.Properties['InstallLocation']) { $key.InstallLocation }
    if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'dotnet' }
    foreach ($candidate in @($env:DOTNET_ROOT_X64, $env:DOTNET_ROOT)) {
        if (![string]::IsNullOrWhiteSpace($candidate)) { $candidate }
    }
    $hostCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $hostCommand) { Split-Path -Parent $hostCommand.Source }
}

function Confirm-WonderRuntime([string]$InstallDirectory) {
    Write-Host ''
    Write-Host 'Wonder needs the Microsoft .NET 10 x64 Runtime.'
    Write-Host "Download .NET $script:WonderRuntimeVersion from Microsoft, verify SHA-512, and install for this user?"
    Write-Host "Location: $InstallDirectory"
    Write-Host 'No administrator access or global PATH changes. This download is separate from Minecraft/Java.'
    return (Read-Host 'Install now? [y/N]') -match '^(y|yes)$'
}

function Install-WonderRuntime([string]$InstallDirectory) {
    $parent = Split-Path -Parent $InstallDirectory
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $stage = Join-Path $parent ('.wonder-install-' + [guid]::NewGuid().ToString('N'))
    $archive = $stage + '.zip'
    try {
        Write-Host "Downloading Microsoft .NET $script:WonderRuntimeVersion..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $script:WonderRuntimeUrl -UseBasicParsing -OutFile $archive -TimeoutSec 180
        $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
        if (![string]::Equals($actual, $script:WonderRuntimeSha512, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The Microsoft runtime SHA-512 check failed. Nothing was installed; run Wonder.cmd again to retry.'
        }
        Write-Host 'SHA-512 verified. Installing the runtime for this user...'
        Expand-Archive -LiteralPath $archive -DestinationPath $stage
        if (!(Test-WonderRuntime $stage)) { throw 'The downloaded runtime could not run on this Windows system.' }
        if (Test-Path -LiteralPath $InstallDirectory) {
            if (!(Test-WonderRuntime $InstallDirectory)) { throw "The runtime folder is incomplete. Rename this folder and retry: $InstallDirectory" }
        } else {
            try { [IO.Directory]::Move($stage, $InstallDirectory) }
            catch { if (!(Test-WonderRuntime $InstallDirectory)) { throw } }
        }
    } finally {
        # Only paths created by this install attempt are removed, never WonderData or another runtime.
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
}

function Resolve-WonderRuntime([string[]]$Candidates, [string]$InstallDirectory) {
    foreach ($candidate in @($Candidates) + @($InstallDirectory) | Select-Object -Unique) {
        if (Test-WonderRuntime $candidate) {
            Write-Host "Using .NET 10: $candidate"
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    if (!(Confirm-WonderRuntime $InstallDirectory)) { Write-Host 'Installation cancelled. No application data was changed.'; return $null }
    Install-WonderRuntime $InstallDirectory
    return [IO.Path]::GetFullPath($InstallDirectory)
}

function Start-Wonder([string]$AppPath, [string]$RuntimeDirectory) {
    $previousRoot = $env:DOTNET_ROOT
    $previousRootX64 = $env:DOTNET_ROOT_X64
    try {
        $env:DOTNET_ROOT = $RuntimeDirectory
        $env:DOTNET_ROOT_X64 = $RuntimeDirectory
        # Keep the application beside its existing WonderData; never extract it to a temp folder.
        return Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -PassThru
    } finally {
        $env:DOTNET_ROOT = $previousRoot
        $env:DOTNET_ROOT_X64 = $previousRootX64
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if (![Environment]::Is64BitOperatingSystem -or [IntPtr]::Size -ne 8) { throw 'This package requires 64-bit Windows and 64-bit Windows PowerShell.' }
        $app = Join-Path $PSScriptRoot 'Wonder.Desktop.exe'
        if (!(Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Extract the complete lightweight ZIP before running Wonder.cmd.' }
        if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { throw 'The current user LocalAppData directory is unavailable.' }
        $install = Join-Path $env:LOCALAPPDATA "Wonder\dotnet\x64\$script:WonderRuntimeVersion"
        $runtime = Resolve-WonderRuntime @(Get-WonderRuntimeCandidates) $install
        if ($null -eq $runtime) { exit 2 }
        $process = Start-Wonder $app $runtime
        Write-Host "Wonder started (process $($process.Id))."
        exit 0
    } catch { Write-Host "Wonder could not start: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
}
