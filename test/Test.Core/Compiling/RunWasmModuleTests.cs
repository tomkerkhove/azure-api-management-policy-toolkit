// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Compiling;

[TestClass]
public class RunWasmModuleTests
{
    [TestMethod]
    [DataRow(
        """
        [Document]
        public class PolicyDocument : IDocument
        {
            public void Inbound(IInboundContext context)
            {
                context.RunWasmModule(new RunWasmModuleConfig
                {
                    ModuleUri = "oci://contoso.azurecr.io/policies/auth-check@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                });
            }
        }
        """,
        """
        <policies>
            <inbound>
                <run-wasm-module module-uri="oci://contoso.azurecr.io/policies/auth-check@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" />
            </inbound>
        </policies>
        """,
        DisplayName = "Should compile run-wasm-module policy in inbound section"
    )]
    [DataRow(
        """
        [Document(Type = DocumentType.Fragment)]
        public class PolicyFragment : IFragment
        {
            public void Fragment(IFragmentContext context)
            {
                context.RunWasmModule(new RunWasmModuleConfig
                {
                    ModuleUri = "oci://contoso.azurecr.io/policies/auth-check:preview"
                });
            }
        }
        """,
        """
        <fragment>
            <run-wasm-module module-uri="oci://contoso.azurecr.io/policies/auth-check:preview" />
        </fragment>
        """,
        DisplayName = "Should compile run-wasm-module policy in a fragment"
    )]
    public void ShouldCompileRunWasmModulePolicy(string code, string expectedXml)
    {
        code.CompileDocument().Should().BeSuccessful().And.DocumentEquivalentTo(expectedXml);
    }

    [TestMethod]
    public void ShouldRejectPolicyExpressionForModuleUri()
    {
        const string Code =
            """
            [Document]
            public class PolicyDocument : IDocument
            {
                public void Inbound(IInboundContext context)
                {
                    context.RunWasmModule(new RunWasmModuleConfig
                    {
                        ModuleUri = ModuleUriExp(context.ExpressionContext)
                    });
                }

                string ModuleUriExp(IExpressionContext context) =>
                    "oci://contoso.azurecr.io/policies/auth-check:preview";
            }
            """;

        var result = Code.CompileDocument();

        result.Errors.Should().ContainSingle(error => error.Id == "APIM9994");
    }
}
