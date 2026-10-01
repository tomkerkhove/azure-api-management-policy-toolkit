// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

public sealed record WasmPolicyResponse
{
    public WasmPolicyResponse(
        int statusCode,
        string statusReason,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? headers = null,
        byte[]? body = null)
    {
        if (statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "The status code must be between 100 and 599.");
        }

        StatusCode = statusCode;
        StatusReason = statusReason ?? throw new ArgumentNullException(nameof(statusReason));
        Headers = headers ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        Body = body ?? [];
    }

    public int StatusCode { get; }

    public string StatusReason { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    public byte[] Body { get; }
}
