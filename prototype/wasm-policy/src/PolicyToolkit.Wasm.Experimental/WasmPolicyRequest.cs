// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;

public sealed record WasmPolicyRequest
{
    public WasmPolicyRequest(
        string method,
        string path,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        byte[]? body)
    {
        Method = method ?? throw new ArgumentNullException(nameof(method));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        ArgumentNullException.ThrowIfNull(headers);
        Headers = headers.ToDictionary(
            header => header.Key,
            header => (IReadOnlyList<string>)header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        Body = body;
    }

    public string Method { get; }

    public string Path { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    public byte[]? Body { get; }

    public bool HasNonEmptyHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Headers.TryGetValue(name, out var values)
            && values.Any(value => !string.IsNullOrWhiteSpace(value));
    }
}
