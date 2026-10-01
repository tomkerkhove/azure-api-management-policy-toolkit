// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

public sealed record WasmPolicyContext(
    WasmPolicyRequest Request,
    IReadOnlyDictionary<string, string> Variables);
