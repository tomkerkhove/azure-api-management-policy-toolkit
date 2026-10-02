// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

public interface IWasmPolicyModule
{
    WasmPolicyDecision OnRequest(WasmPolicyContext context);
}
