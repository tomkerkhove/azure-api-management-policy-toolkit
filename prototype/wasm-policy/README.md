# WASM policy module prototype

This source-only prototype explores a separate .NET authoring model for APIM
`run-wasm-module` guests. It does not change the existing C#-to-XML Policy Toolkit
compiler into a WebAssembly compiler.

Read the complete [customer journey and current blocker](../../docs/WasmPolicyModules.md)
before using the build scripts.

## Validate locally

```powershell
rustup toolchain install 1.94.1 --profile minimal
.\eng\Install-Tools.ps1

$env:PATH = "$(Resolve-Path .\.tools\bin);$(Resolve-Path .\.tools\oras-1.3.0);$env:PATH"
$Oras = Resolve-Path .\.tools\oras-1.3.0\oras.exe
.\eng\Build-Prototype.ps1 -OrasPath $Oras
```

The build is expected to report
`CompatibleWithCurrentApimHost : False`. The generated component has ambient WASI
imports and must not be deployed.
