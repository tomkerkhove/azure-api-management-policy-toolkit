// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Documentation;

[TestClass]
public sealed class WasmPolicyContractTests
{
    private const string PackageIdentity = "microsoft:azure-api-management-gateway@0.1.0";
    private const string PreviousPackageIdentity = "demo" + ":policy@0.2.0";
    private const string WitSha256 = "bacee3668ba4d8aabad2890660f8f586148ea9a3860210d463615d84a00d1507";

    [TestMethod]
    public void ShouldUseAuthoritativeApimWitIdentity()
    {
        var root = GetRepositoryRoot();
        var witPath = Path.Combine(root, "prototype", "wasm-policy", "wit", "policy.wit");
        var provenancePath = Path.Combine(root, "prototype", "wasm-policy", "wit", "provenance.json");
        var versionsPath = Path.Combine(root, "prototype", "wasm-policy", "eng", "versions.json");
        var handlerPath = Path.Combine(
            root,
            "prototype",
            "wasm-policy",
            "samples",
            "AuthCheck.Component",
            "HandlerExportsImpl.cs");

        var wit = File.ReadAllText(witPath).Replace("\r\n", "\n");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wit))).ToLowerInvariant();
        using var provenance = JsonDocument.Parse(File.ReadAllText(provenancePath));
        using var versions = JsonDocument.Parse(File.ReadAllText(versionsPath));
        var handler = File.ReadAllText(handlerPath);

        wit.Should().StartWith($"package {PackageIdentity};");
        digest.Should().Be(WitSha256);
        provenance.RootElement.GetProperty("package").GetString().Should().Be(PackageIdentity);
        provenance.RootElement.GetProperty("sha256").GetString().Should().Be(WitSha256);
        versions.RootElement.GetProperty("apim").GetProperty("witPackage").GetString().Should().Be(PackageIdentity);
        versions.RootElement.GetProperty("apim").GetProperty("witSha256").GetString().Should().Be(WitSha256);
        handler.Should().Contain("microsoft.azureApiManagementGateway.v0_1_0");

        foreach (var path in EnumerateContractSources(root))
        {
            File.ReadAllText(path).Should().NotContain(PreviousPackageIdentity);
        }
    }

    private static IEnumerable<string> EnumerateContractSources(string root)
    {
        var paths = new[]
        {
            Path.Combine(root, "docs"),
            Path.Combine(root, "prototype", "wasm-policy")
        };

        return paths.SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".cs" or ".json" or ".md" or ".ps1" or ".wit")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.tools{Path.DirectorySeparatorChar}"));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "apim-policy-toolkit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
