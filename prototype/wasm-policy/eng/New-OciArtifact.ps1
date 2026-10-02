# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AotPath,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$')]
    [string]$Created,

    [string]$Tag = 'prototype',

    [string]$OrasPath = 'oras'
)

$ErrorActionPreference = 'Stop'
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
$AotPath = [System.IO.Path]::GetFullPath($AotPath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath $AotPath -PathType Leaf))
{
    throw "The AOT component '$AotPath' does not exist."
}

$aot = Get-Item -LiteralPath $AotPath
if ($aot.Length -gt $versions.apim.maxLayerBytes)
{
    throw "The AOT component is $($aot.Length) bytes; APIM accepts at most $($versions.apim.maxLayerBytes) bytes."
}

if (Test-Path -LiteralPath $OutputPath)
{
    throw "The output path '$OutputPath' already exists."
}

$orasVersion = & $OrasPath version 2>$null | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or $orasVersion -notmatch [regex]::Escape($versions.oras.version))
{
    throw "ORAS '$($versions.oras.version)' is required; found '$orasVersion'."
}

$target = "${OutputPath}:$Tag"
$layer = "$($aot.Name):$($versions.apim.layerMediaType)"
Push-Location $aot.DirectoryName
try
{
    $output = & $OrasPath push `
        --oci-layout $target `
        $layer `
        --artifact-type $versions.apim.artifactMediaType `
        --annotation "org.opencontainers.image.created=$Created" `
        --format json
    if ($LASTEXITCODE -ne 0)
    {
        throw "ORAS failed to create '$target'."
    }
}
finally
{
    Pop-Location
}

$result = ($output -join [Environment]::NewLine) | ConvertFrom-Json
if ($result.digest -notmatch '^sha256:[0-9a-f]{64}$')
{
    throw "ORAS returned an invalid manifest digest '$($result.digest)'."
}

[pscustomobject]@{
    Layout = $OutputPath
    Tag = $Tag
    Digest = $result.digest
    LocalLayoutReference = "oci-layout:$OutputPath@$($result.digest)"
}
