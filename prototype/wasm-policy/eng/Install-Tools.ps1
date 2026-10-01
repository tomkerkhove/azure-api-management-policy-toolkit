# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$ToolRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) '.tools')
)

$ErrorActionPreference = 'Stop'
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)
$cacheRoot = Join-Path $ToolRoot 'cache'
$sourceRoot = Join-Path $ToolRoot 'src'
New-Item -ItemType Directory -Path $cacheRoot, $sourceRoot -Force | Out-Null

if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64')
{
    throw 'The hash-verified tool bootstrap currently supports Windows x64 only.'
}

function Get-VerifiedDownload
{
    param(
        [Parameter(Mandatory)]
        [string]$Uri,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Sha256
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        Invoke-WebRequest -Uri $Uri -OutFile $Path
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256)
    {
        throw "The SHA-256 digest for '$Path' is '$actual'; expected '$Sha256'."
    }
}

function Install-VerifiedCargoTool
{
    param(
        [Parameter(Mandatory)]
        [string]$Package,

        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [string]$Sha256
    )

    $archive = Join-Path $cacheRoot "$Package-$Version.crate"
    Get-VerifiedDownload `
        -Uri "https://crates.io/api/v1/crates/$Package/$Version/download" `
        -Path $archive `
        -Sha256 $Sha256

    $source = Join-Path $sourceRoot "$Package-$Version"
    if (-not (Test-Path -LiteralPath $source -PathType Container))
    {
        tar.exe -xzf $archive -C $sourceRoot
        if ($LASTEXITCODE -ne 0)
        {
            throw "Failed to extract '$archive'."
        }
    }

    cargo "+$($versions.rustToolchain)" install `
        --locked `
        --path $source `
        --root $ToolRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to install $Package $Version."
    }
}

$sdkVersion = dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne $versions.dotnetSdk)
{
    throw ".NET SDK '$($versions.dotnetSdk)' is required; found '$sdkVersion'."
}

$testRuntime = dotnet --list-runtimes |
    Where-Object { $_ -match "^Microsoft\.NETCore\.App $([regex]::Escape($versions.dotnetTestRuntime))\." }
if (-not $testRuntime)
{
    throw ".NET runtime '$($versions.dotnetTestRuntime).x' is required by the ordinary policy tests."
}

rustup run $versions.rustToolchain rustc --version | Out-Null
if ($LASTEXITCODE -ne 0)
{
    throw "Rust '$($versions.rustToolchain)' is required. Install it with 'rustup toolchain install $($versions.rustToolchain) --profile minimal'."
}

Install-VerifiedCargoTool `
    -Package $versions.wasmTools.package `
    -Version $versions.wasmTools.version `
    -Sha256 $versions.wasmTools.crateSha256
Install-VerifiedCargoTool `
    -Package $versions.hyperlightWasmAot.package `
    -Version $versions.hyperlightWasmAot.version `
    -Sha256 $versions.hyperlightWasmAot.crateSha256

$orasArchive = Join-Path $cacheRoot "oras-$($versions.oras.version)-windows-amd64.zip"
Get-VerifiedDownload `
    -Uri $versions.oras.windowsAmd64Url `
    -Path $orasArchive `
    -Sha256 $versions.oras.windowsAmd64Sha256

$orasRoot = Join-Path $ToolRoot "oras-$($versions.oras.version)"
$orasPath = Join-Path $orasRoot 'oras.exe'
if (-not (Test-Path -LiteralPath $orasPath -PathType Leaf))
{
    Expand-Archive -LiteralPath $orasArchive -DestinationPath $orasRoot -Force
}

Write-Output "Installed pinned tools under '$ToolRoot'."
Write-Output "Add '$ToolRoot\bin' and '$orasRoot' to PATH for this shell."
