// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Documentation;

[TestClass]
public sealed class WasmPolicyQuickstartTests
{
    private static readonly string[] s_forbiddenContent =
    [
        "cargo",
        "rustup",
        "git clone",
        "git checkout",
        "from the repository root",
        "dotnet workload install",
        ".ps1",
        "Build-Prototype",
        "Install-Tools",
        "Initialize-PrototypeTools",
        "Test-OciArtifact",
        "Test-OciLayout"
    ];

    private static readonly string[] s_requiredContent =
    [
        "dotnet test",
        "dotnet build",
        "BytecodeAlliance.Componentize.DotNet.Wasm.SDK",
        "wasm-tools component wit",
        "wasm-tools component targets",
        "hyperlight-wasm-aot compile",
        "oras push",
        "not deployable",
        "target contract only",
        "18 ambient WASI imports"
    ];

    [TestMethod]
    public void ShouldRemainDotNetFirstAndFailClosed()
    {
        var quickstart = File.ReadAllText(GetQuickstartPath());

        foreach (var forbidden in s_forbiddenContent)
        {
            quickstart.Should().NotContainEquivalentOf(forbidden);
        }

        foreach (var required in s_requiredContent)
        {
            quickstart.Should().ContainEquivalentOf(required);
        }
    }

    private static string GetQuickstartPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "apim-policy-toolkit.sln");
            if (File.Exists(solutionPath))
            {
                return Path.Combine(directory.FullName, "docs", "WasmPolicyQuickstart.md");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
