// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Authoring;

/// <summary>
/// Configuration for the run-wasm-module policy.<br/>
/// Executes an OCI-packaged WebAssembly policy module.
/// </summary>
public record RunWasmModuleConfig
{
    /// <summary>
    /// Required. The literal OCI reference for the module in Azure Container Registry.
    /// Policy expressions are not allowed.
    /// </summary>
    public required string ModuleUri { get; init; }
}
