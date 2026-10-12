// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

public sealed record WasmPolicyDecision
{
    private WasmPolicyDecision(bool isAllowed, WasmPolicyResponse? response)
    {
        IsAllowed = isAllowed;
        Response = response;
    }

    public bool IsAllowed { get; }

    public WasmPolicyResponse? Response { get; }

    public static WasmPolicyDecision Allow() => new(true, null);

    public static WasmPolicyDecision Reject(WasmPolicyResponse response) =>
        new(false, response ?? throw new ArgumentNullException(nameof(response)));
}
