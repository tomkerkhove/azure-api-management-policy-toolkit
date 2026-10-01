// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using AuthCheck;

using FluentAssertions;

using Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuthCheck.Tests;

[TestClass]
public sealed class AuthCheckPolicyTests
{
    [TestMethod]
    public void AllowsRequestWithAuthorizationHeader()
    {
        var decision = Evaluate(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = new[] { "Bearer example" }
            });

        decision.IsAllowed.Should().BeTrue();
        decision.Response.Should().BeNull();
    }

    [TestMethod]
    public void HeaderLookupIsCaseInsensitive()
    {
        var decision = Evaluate(
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["authorization"] = new[] { "Bearer example" }
            });

        decision.IsAllowed.Should().BeTrue();
    }

    [TestMethod]
    public void RejectsRequestWithoutAuthorizationHeader()
    {
        var decision = Evaluate(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

        decision.IsAllowed.Should().BeFalse();
        decision.Response.Should().NotBeNull();
        decision.Response!.StatusCode.Should().Be(401);
        decision.Response.StatusReason.Should().Be("Unauthorized");
        decision.Response.Headers["WWW-Authenticate"].Should().Equal("Bearer");
        decision.Response.Body.Should().BeEmpty();
    }

    [TestMethod]
    public void RejectsEmptyAuthorizationHeader()
    {
        var decision = Evaluate(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = new[] { string.Empty }
            });

        decision.IsAllowed.Should().BeFalse();
    }

    private static WasmPolicyDecision Evaluate(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        var policy = new AuthCheckPolicy();
        return policy.OnRequest(
            new WasmPolicyContext(
                new WasmPolicyRequest("GET", "/", headers, null),
                new Dictionary<string, string>(StringComparer.Ordinal)));
    }
}
