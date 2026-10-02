# WASM policy module prototype

This source-only prototype explores a separate .NET authoring model for APIM
`run-wasm-module` guests. It does not change the existing C#-to-XML Policy Toolkit
compiler into a WebAssembly compiler.

Start with the [customer quickstart](../../docs/WasmPolicyQuickstart.md), then use
the [technical guide](../../docs/WasmPolicyModules.md) for API, build, validation,
and troubleshooting details.

## Build with .NET

```powershell
dotnet restore .\WasmPolicyPrototype.slnx --locked-mode

dotnet test .\test\AuthCheck.Tests\AuthCheck.Tests.csproj `
    --configuration Release `
    --no-restore

dotnet build .\samples\AuthCheck.Component\AuthCheck.Component.csproj `
    --configuration Release `
    --no-restore `
    --no-incremental
```

The component is written to:

```text
samples/AuthCheck.Component/bin/Release/net10.0/wasi-wasm/publish/auth_check.wasm
```

Follow the [technical guide](../../docs/WasmPolicyModules.md) to install the
official prebuilt `wasm-tools` binary and run the exact APIM world check. It
currently fails because the generated component has ambient WASI imports and must
not be deployed.

## Repository-maintainer automation

The `eng` scripts retain reproducible tool bootstrap, AOT, and OCI validation for
repository maintenance and CI. They are not required by the .NET library-user
flow.
