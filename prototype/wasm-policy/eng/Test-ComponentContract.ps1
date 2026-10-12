# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ComponentPath,

    [switch]$AllowUnexpectedImports,

    [string]$WitPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'wit'),

    [string]$ContractOutputPath
)

$ErrorActionPreference = 'Stop'
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
$ComponentPath = [System.IO.Path]::GetFullPath($ComponentPath)
$WitPath = [System.IO.Path]::GetFullPath($WitPath)
if (-not (Test-Path -LiteralPath $ComponentPath -PathType Leaf))
{
    throw "The component '$ComponentPath' does not exist."
}
if (-not (Test-Path -LiteralPath $WitPath))
{
    throw "The WIT contract '$WitPath' does not exist."
}

$wasmToolsVersion = wasm-tools --version
if ($LASTEXITCODE -ne 0 -or $wasmToolsVersion -notmatch [regex]::Escape($versions.wasmTools.version))
{
    throw "wasm-tools '$($versions.wasmTools.version)' is required; found '$wasmToolsVersion'."
}

wasm-tools validate $ComponentPath
if ($LASTEXITCODE -ne 0)
{
    throw "wasm-tools could not validate '$ComponentPath'."
}

$contract = wasm-tools component wit $ComponentPath
if ($LASTEXITCODE -ne 0)
{
    throw "wasm-tools could not inspect '$ComponentPath'."
}

if (-not [string]::IsNullOrWhiteSpace($ContractOutputPath))
{
    $output = [System.IO.Path]::GetFullPath($ContractOutputPath)
    New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force | Out-Null
    $contract | Set-Content -LiteralPath $output -Encoding utf8NoBOM
}

$imports = @(
    $contract |
        Select-String -Pattern '^\s+import (?<name>[^;]+);$' |
        ForEach-Object { $_.Matches[0].Groups['name'].Value }
)
$allowedImports = @(
    'microsoft:azure-api-management-gateway/api-inspector@0.1.0',
    'microsoft:azure-api-management-gateway/types@0.1.0'
)
$unexpectedImports = @($imports | Where-Object { $_ -notin $allowedImports })
$exports = @(
    $contract |
        Select-String -Pattern '^\s+export (?<name>[^;]+);$' |
        ForEach-Object { $_.Matches[0].Groups['name'].Value }
)

$targetValidation = @(
    wasm-tools component targets -w policy $WitPath $ComponentPath 2>&1
)
$compatibleWithCurrentApimHost = $LASTEXITCODE -eq 0

$result = [pscustomobject]@{
    CompatibleWithCurrentApimHost = $compatibleWithCurrentApimHost
    Imports = $imports
    UnexpectedImports = $unexpectedImports
    Exports = $exports
    TargetValidation = $targetValidation
}

if (-not $compatibleWithCurrentApimHost -and -not $AllowUnexpectedImports)
{
    $details = $targetValidation -join [Environment]::NewLine
    throw "The component does not target the exact APIM policy world: $details"
}

$result
