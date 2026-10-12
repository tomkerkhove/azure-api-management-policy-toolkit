# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$OrasPath = 'oras',

    [ValidatePattern('^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$')]
    [string]$Created = '2026-10-01T00:00:00Z'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
$artifacts = Join-Path $root 'artifacts'
$componentProject = Join-Path $root 'samples/AuthCheck.Component/AuthCheck.Component.csproj'
$componentBuild = Join-Path $root 'samples/AuthCheck.Component/bin/Release/net10.0/wasi-wasm/publish/auth_check.wasm'
$component = Join-Path $artifacts 'auth_check.component.wasm'
$contract = Join-Path $artifacts 'auth_check.component.wit'
$aot = Join-Path $artifacts 'auth_check.aot'
$layout = Join-Path $artifacts 'auth-check-oci'
$witPath = Join-Path $root 'wit/policy.wit'

$sdkVersion = dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne $versions.dotnetSdk)
{
    throw ".NET SDK '$($versions.dotnetSdk)' is required; found '$sdkVersion'."
}

$aotVersion = hyperlight-wasm-aot --version
if ($LASTEXITCODE -ne 0 -or $aotVersion -notmatch [regex]::Escape($versions.hyperlightWasmAot.version))
{
    throw "hyperlight-wasm-aot '$($versions.hyperlightWasmAot.version)' is required; found '$aotVersion'."
}

$witContent = [System.IO.File]::ReadAllText($witPath).Replace("`r`n", "`n")
$witSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($witContent))).ToLowerInvariant()
if ($witSha256 -ne $versions.apim.witSha256)
{
    throw "The policy.wit SHA-256 digest is '$witSha256'; expected '$($versions.apim.witSha256)'."
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
if (Test-Path -LiteralPath $layout)
{
    Remove-Item -LiteralPath $layout -Recurse -Force
}

dotnet restore (Join-Path $root 'WasmPolicyPrototype.slnx') --locked-mode
if ($LASTEXITCODE -ne 0)
{
    throw 'Package restore failed.'
}

dotnet test `
    (Join-Path $root 'test/AuthCheck.Tests/AuthCheck.Tests.csproj') `
    --configuration Release `
    --no-restore
if ($LASTEXITCODE -ne 0)
{
    throw 'Policy logic tests failed.'
}

dotnet build $componentProject --configuration Release --no-restore --no-incremental
if ($LASTEXITCODE -ne 0)
{
    throw 'The experimental .NET component build failed.'
}

Copy-Item -LiteralPath $componentBuild -Destination $component -Force
$compatibility = & (Join-Path $PSScriptRoot 'Test-ComponentContract.ps1') `
    -ComponentPath $component `
    -ContractOutputPath $contract `
    -AllowUnexpectedImports

hyperlight-wasm-aot compile --component $component $aot
if ($LASTEXITCODE -ne 0)
{
    throw 'Hyperlight AOT compilation failed.'
}

$package = & (Join-Path $PSScriptRoot 'New-OciArtifact.ps1') `
    -AotPath $aot `
    -OutputPath $layout `
    -Created $Created `
    -OrasPath $OrasPath
$oci = & (Join-Path $PSScriptRoot 'Test-OciArtifact.ps1') `
    -LayoutPath $layout `
    -OrasPath $OrasPath

[pscustomobject]@{
    ComponentPath = $component
    ComponentSha256 = (Get-FileHash -LiteralPath $component -Algorithm SHA256).Hash.ToLowerInvariant()
    AotPath = $aot
    AotSha256 = (Get-FileHash -LiteralPath $aot -Algorithm SHA256).Hash.ToLowerInvariant()
    CompatibleWithCurrentApimHost = $compatibility.CompatibleWithCurrentApimHost
    UnexpectedImports = $compatibility.UnexpectedImports
    OciManifestDigest = $oci.ManifestDigest
    LocalOciLayoutReference = $package.LocalLayoutReference
}
