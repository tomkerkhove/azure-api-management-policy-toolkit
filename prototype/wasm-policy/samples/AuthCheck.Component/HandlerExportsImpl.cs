// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using AuthCheck;

using Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

using Types = PolicyWorld.wit.Imports.demo.policy.v0_2_0.ITypesImports;

namespace PolicyWorld.wit.Exports.demo.policy.v0_2_0;

public sealed class HandlerExportsImpl : IHandlerExports
{
    public static Types.Decision OnRequest(Types.Context context)
    {
        var policy = new AuthCheckPolicy();
        var decision = policy.OnRequest(
            new WasmPolicyContext(
                new WasmPolicyRequest(
                    context.request.method,
                    context.request.url.path,
                    MapHeaders(context.request.headers),
                    context.request.body),
                MapVariables(context.variables)));

        if (decision.IsAllowed)
        {
            return Types.Decision.Allow();
        }

        var response = decision.Response
            ?? throw new InvalidOperationException("A rejected decision must include a response.");
        return Types.Decision.Reject(
            new Types.Response(
                response.Body,
                response.Headers
                    .Select(header => new Types.StringValues(header.Key, [.. header.Value]))
                    .ToList(),
                response.StatusCode,
                response.StatusReason));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> MapHeaders(
        IEnumerable<Types.StringValues> headers)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.name] = header.values;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> MapVariables(
        IEnumerable<Types.StringProperty> variables)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            result[variable.name] = variable.value;
        }

        return result;
    }
}
