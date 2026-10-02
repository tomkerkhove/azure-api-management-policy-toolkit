# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$LayoutPath,

    [string]$Tag = 'prototype',

    [string]$OrasPath = 'oras'
)

$ErrorActionPreference = 'Stop'
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
$LayoutPath = [System.IO.Path]::GetFullPath($LayoutPath)
if (-not (Test-Path -LiteralPath (Join-Path $LayoutPath 'oci-layout') -PathType Leaf))
{
    throw "'$LayoutPath' is not an OCI image layout."
}

$reference = "${LayoutPath}:$Tag"
$manifestOutput = & $OrasPath manifest fetch --oci-layout $reference
if ($LASTEXITCODE -ne 0)
{
    throw "ORAS could not read '$reference'."
}

$descriptorOutput = & $OrasPath manifest fetch --oci-layout --descriptor $reference
if ($LASTEXITCODE -ne 0)
{
    throw "ORAS could not read the descriptor for '$reference'."
}

$manifest = ($manifestOutput -join [Environment]::NewLine) | ConvertFrom-Json
$descriptor = ($descriptorOutput -join [Environment]::NewLine) | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2)
{
    throw "The OCI schema version is '$($manifest.schemaVersion)'; expected 2."
}
if ($manifest.mediaType -ne $versions.apim.manifestMediaType)
{
    throw "The OCI manifest media type is '$($manifest.mediaType)'; expected '$($versions.apim.manifestMediaType)'."
}
if ($manifest.artifactType -ne $versions.apim.artifactMediaType)
{
    throw "The OCI artifact type is '$($manifest.artifactType)'; expected '$($versions.apim.artifactMediaType)'."
}
if ($manifest.config.mediaType -ne $versions.apim.configMediaType)
{
    throw "The OCI config media type is '$($manifest.config.mediaType)'; expected '$($versions.apim.configMediaType)'."
}
if ($manifest.layers.Count -ne 1)
{
    throw "The OCI artifact contains $($manifest.layers.Count) layers; expected exactly one."
}

$layer = $manifest.layers[0]
if ($layer.mediaType -ne $versions.apim.layerMediaType)
{
    throw "The OCI layer media type is '$($layer.mediaType)'; expected '$($versions.apim.layerMediaType)'."
}
if ($layer.size -lt 0 -or $layer.size -gt $versions.apim.maxLayerBytes)
{
    throw "The OCI layer is $($layer.size) bytes; expected 0-$($versions.apim.maxLayerBytes) bytes."
}
if ($layer.digest -notmatch '^sha256:(?<hash>[0-9a-f]{64})$')
{
    throw "The OCI layer digest '$($layer.digest)' is invalid."
}

$blobPath = Join-Path $LayoutPath "blobs/sha256/$($Matches.hash)"
if (-not (Test-Path -LiteralPath $blobPath -PathType Leaf))
{
    throw "The OCI layer blob '$blobPath' does not exist."
}

$blob = Get-Item -LiteralPath $blobPath
$blobHash = (Get-FileHash -LiteralPath $blobPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($blob.Length -ne $layer.size -or $blobHash -ne $Matches.hash)
{
    throw 'The OCI layer size or digest does not match its descriptor.'
}
if ($descriptor.digest -notmatch '^sha256:[0-9a-f]{64}$')
{
    throw "The OCI manifest digest '$($descriptor.digest)' is invalid."
}

[pscustomobject]@{
    ManifestDigest = $descriptor.digest
    ManifestMediaType = $manifest.mediaType
    ArtifactType = $manifest.artifactType
    ConfigMediaType = $manifest.config.mediaType
    LayerMediaType = $layer.mediaType
    LayerDigest = $layer.digest
    LayerSize = $layer.size
    LocalLayoutReference = "oci-layout:$LayoutPath@$($descriptor.digest)"
}
