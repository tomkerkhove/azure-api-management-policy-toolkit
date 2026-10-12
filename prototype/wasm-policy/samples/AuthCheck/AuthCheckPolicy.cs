// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

namespace AuthCheck;

public sealed class AuthCheckPolicy : IWasmPolicyModule
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> s_rejectionHeaders =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["WWW-Authenticate"] = new[] { "Bearer" }
        };

    public WasmPolicyDecision OnRequest(WasmPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.HasNonEmptyHeader("Authorization"))
        {
            return WasmPolicyDecision.Allow();
        }

        return WasmPolicyDecision.Reject(
            new WasmPolicyResponse(
                statusCode: 401,
                statusReason: "Unauthorized",
                headers: s_rejectionHeaders));
    }
}
