// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Xml.Linq;

using Microsoft.Azure.ApiManagement.PolicyToolkit.Authoring;
using Microsoft.Azure.ApiManagement.PolicyToolkit.Compiling.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Azure.ApiManagement.PolicyToolkit.Compiling.Policy;

public class RunWasmModuleCompiler : IMethodPolicyHandler
{
    public string MethodName => nameof(IInboundContext.RunWasmModule);

    public void Handle(IDocumentCompilationContext context, InvocationExpressionSyntax node)
    {
        if (!node.TryExtractingConfigParameter<RunWasmModuleConfig>(
                context,
                "run-wasm-module",
                out var values))
        {
            return;
        }

        var element = new XElement("run-wasm-module");
        if (!values.TryGetValue(nameof(RunWasmModuleConfig.ModuleUri), out var moduleUri))
        {
            context.Report(Diagnostic.Create(
                CompilationErrors.RequiredParameterNotDefined,
                node.GetLocation(),
                "run-wasm-module",
                nameof(RunWasmModuleConfig.ModuleUri)
            ));
            return;
        }

        if (moduleUri.Node is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            context.Report(Diagnostic.Create(
                CompilationErrors.ValueShouldBe,
                moduleUri.Node.GetLocation(),
                "run-wasm-module",
                "a string literal"
            ));
            return;
        }

        element.Add(new XAttribute("module-uri", moduleUri.Value!));
        context.AddPolicy(element);
    }
}
