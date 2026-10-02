# .NET WASM policy module technical guide

This guide is for .NET developers evaluating the source-only
`Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm` prototype. Start with the
[customer quickstart](WasmPolicyQuickstart.md) if you want the shorter APIM
journey.

> [!CAUTION]
> The prototype supports local authoring, tests, component generation, AOT
> compilation, and OCI packaging. Its current Componentize.NET output is **not
> compatible with the APIM Hyperlight host** because it imports WASI interfaces
> the host does not provide. Do not publish or configure the generated artifact.

## How the two Toolkit models differ

The existing Policy Toolkit is an XML authoring system. It parses `IDocument`
classes and section-method calls with Roslyn, then emits an APIM XML policy
document. That C# does not execute in the gateway.

The experimental WASM API is a separate executable model. An
`IWasmPolicyModule` reads a limited request/context model and returns allow or
reject. `RunWasmModuleConfig` remains part of the XML model: it only emits the
`<run-wasm-module>` element that points to a packaged module.

| Model | Customer code | Output |
| --- | --- | --- |
| Existing XML policy DSL | `IDocument`, `IInboundContext`, policy methods | APIM XML |
| Experimental module API | `IWasmPolicyModule.OnRequest` | WebAssembly component, then Hyperlight AOT |

Existing policy methods such as `SetHeader`, `SendRequest`, and `CacheLookup`
cannot be called from a module or compiled into one.

## Use the source prototype

The API is not published as a NuGet package or template. It is intentionally
isolated from the shipping solution and packages under:

```text
prototype/wasm-policy/
├── src/PolicyToolkit.Wasm.Experimental/
├── samples/AuthCheck/
├── samples/AuthCheck.Component/
├── test/AuthCheck.Tests/
├── wit/
└── eng/
```

Project references connect the sample to the source API:

```xml
<ProjectReference
    Include="..\..\src\PolicyToolkit.Wasm.Experimental\PolicyToolkit.Wasm.Experimental.csproj" />
```

The component project separately references the policy logic and uses generated
WIT bindings to expose it as the APIM `handler` export.

## Prerequisites

The documented local path for this prototype is Windows x64 with PowerShell 7:

- .NET SDK `10.0.400`
- .NET runtime `8.0`
- `rustup`
- access to the public NuGet, crates.io, Rust, LLVM, GitHub release, and
  `dotnet-experimental` endpoints used by the pinned dependencies

Install the pinned tools:

```powershell
cd prototype\wasm-policy

rustup toolchain install 1.94.1 --profile minimal
.\eng\Install-Tools.ps1

$env:PATH = "$(Resolve-Path .\.tools\bin);$(Resolve-Path .\.tools\oras-1.3.0);$env:PATH"
$Oras = Resolve-Path .\.tools\oras-1.3.0\oras.exe
```

`Install-Tools.ps1` verifies the downloaded `wasm-tools`,
`hyperlight-wasm-aot`, and ORAS archives before installing them under `.tools`.
Exact versions and hashes are centralized in
[`eng/versions.json`](../prototype/wasm-policy/eng/versions.json).

## Authoring model

Implement one method:

```csharp
public interface IWasmPolicyModule
{
    WasmPolicyDecision OnRequest(WasmPolicyContext context);
}
```

The authorization sample is:

```csharp
public sealed class AuthCheckPolicy : IWasmPolicyModule
{
    public WasmPolicyDecision OnRequest(WasmPolicyContext context)
    {
        if (context.Request.HasNonEmptyHeader("Authorization"))
        {
            return WasmPolicyDecision.Allow();
        }

        return WasmPolicyDecision.Reject(
            new WasmPolicyResponse(
                statusCode: 401,
                statusReason: "Unauthorized",
                headers: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["WWW-Authenticate"] = new[] { "Bearer" }
                }));
    }
}
```

### Prototype API surface

`WasmPolicyContext` currently exposes:

```csharp
public sealed record WasmPolicyContext(
    WasmPolicyRequest Request,
    IReadOnlyDictionary<string, string> Variables);
```

`WasmPolicyRequest` exposes:

- `Method`
- `Path`
- case-insensitive multi-value `Headers`
- optional binary `Body`
- `HasNonEmptyHeader(name)`

The small Toolkit model is intentionally narrower than the WIT contract. Extend
the adapter and source API together when adding fields; do not copy generated WIT
types into hand-written code.

`WasmPolicyDecision` provides:

- `Allow()` — continue the policy pipeline.
- `Reject(WasmPolicyResponse)` — fail normal processing with an HTTP response.

A rejection response contains a status code from `100` through `599`, a reason,
multi-value headers, and a binary body. APIM removes hop-by-hop response headers
and `Content-Length`, and handles `Content-Type` separately. Rejections follow
normal `on-error` processing.

### Host capabilities

The module receives data; it does not receive a general APIM client. The current
contract cannot:

- send HTTP requests;
- read named values;
- use files, sockets, environment variables, random data, or host clocks;
- mutate the APIM request;
- persist module globals or policy variables between requests.

The only callable host function is an inspector log operation represented by the
generated WIT bindings. The sample policy API does not wrap it yet.

## Unit test policy logic

Keep decision logic independent from generated bindings and run it as normal .NET:

```powershell
dotnet test .\test\AuthCheck.Tests\AuthCheck.Tests.csproj --configuration Release
```

The checked-in tests cover the allow and reject responses, case-insensitive
headers, and empty authorization values. Add logic tests here before testing the
component boundary.

## Generated WIT bindings

[`wit/policy.wit`](../prototype/wasm-policy/wit/policy.wit) is the single
authoritative guest contract in this prototype. Its
[`provenance.json`](../prototype/wasm-policy/wit/provenance.json) records its
origin, ABI, and normalized SHA-256 digest.

The Componentize.NET project includes that directory:

```xml
<Wit Include="../../wit" World="policy" />
```

The build generates C# imports, exports, and interop types under `obj`. Do not
check those generated files into source and do not recreate WIT records manually.
The adapter in
[`HandlerExportsImpl.cs`](../prototype/wasm-policy/samples/AuthCheck.Component/HandlerExportsImpl.cs)
maps generated request types to the small Toolkit model and maps the decision
back to the generated response types.

The WIT contract matters because APIM links a module by exact, versioned imports
and exports. Matching C# type names or producing a valid `.wasm` file is not
enough.

## Build the component, AOT, and OCI layout

Run the high-level build:

```powershell
.\eng\Build-Prototype.ps1 -OrasPath $Oras
```

It performs these steps:

1. verify the .NET SDK and authoritative WIT digest;
2. restore locked NuGet dependencies;
3. run the ordinary policy tests;
4. generate bindings and build `auth_check.component.wasm`;
5. validate and inspect the component;
6. compile `auth_check.aot` with the pinned Hyperlight AOT compiler;
7. create and validate a local single-layer OCI layout.

The result summary includes component, AOT, and OCI manifest digests and:

```text
CompatibleWithCurrentApimHost : False
```

That compatibility result is expected today. `Build-Prototype.ps1` finishes so
developers can inspect every stage, but its zero exit code is not a deployment
approval.

## Validation gates

### 1. WIT source digest

The build normalizes CRLF to LF, calculates SHA-256 for `policy.wit`, and compares
it with `eng/versions.json`. A mismatch stops the build before bindings are used.

### 2. Exact APIM component world

Run the fail-closed deployment gate:

```powershell
.\eng\Test-ComponentContract.ps1 `
    -ComponentPath .\artifacts\auth_check.component.wasm
```

The script runs `wasm-tools validate` and `wasm-tools component targets` against
the authoritative `policy` world. It currently fails because Componentize.NET
adds 18 WASI Preview 2 imports. This failure is correct.

For local diagnosis only, capture the generated contract while allowing the known
incompatibility:

```powershell
.\eng\Test-ComponentContract.ps1 `
    -ComponentPath .\artifacts\auth_check.component.wasm `
    -ContractOutputPath .\artifacts\auth_check.component.wit `
    -AllowUnexpectedImports
```

Never use `-AllowUnexpectedImports` as a deployment gate.

### 3. Hyperlight AOT

The build compiles the component with pinned `hyperlight-wasm-aot` for
`x86_64-unknown-none`. A successful compile proves only that the AOT compiler
accepted the component. It does not prove the APIM host can link or safely execute
it.

An ordinary .NET assembly, the portable `.wasm` component, or output from a
different AOT/runtime version is not an APIM module.

### 4. OCI artifact

`Test-OciArtifact.ps1` validates:

- OCI image manifest schema `2`;
- manifest media type `application/vnd.oci.image.manifest.v1+json`;
- artifact and single-layer media type
  `application/vnd.hyperlight.policy.v1+aot`;
- empty config media type `application/vnd.oci.empty.v1+json`;
- exactly one layer no larger than `67,108,864` bytes;
- manifest, descriptor, blob size, and SHA-256 consistency.

Re-run it directly when inspecting an existing local layout:

```powershell
.\eng\Test-OciArtifact.ps1 `
    -LayoutPath .\artifacts\auth-check-oci `
    -OrasPath $Oras
```

### 5. Real runtime

A deployable implementation must also prove allow, reject, interruption,
recovery, snapshot reset, and disposal on the exact Windows Hypervisor Platform
and Hyperlight runtime used by APIM. This repository does not currently provide
that passing .NET test.

## Current runtime limitation

The Componentize.NET SDK emits ambient `wasi:io`, `wasi:clocks`, `wasi:cli`,
`wasi:filesystem`, and `wasi:random` imports even though the sample does not use
those capabilities. APIM intentionally exposes only the policy interfaces, so the
component cannot link.

A deny-by-default compatibility-adapter experiment removed those imports, but the
bounded component faulted on its first real Hyperlight request. The recommended
action is to keep policy logic and unit tests in this prototype, but stop promotion
at `Test-ComponentContract.ps1` until the upstream .NET component toolchain can
emit the exact APIM world or a bounded adapter passes all real-runtime gates.

Do not expand the APIM host with broad filesystem, environment, clock, random, or
stdio capabilities merely to make the current component instantiate.

## Package and publish contract

> [!WARNING]
> The current .NET artifact is incompatible. The commands in the
> [customer quickstart](WasmPolicyQuickstart.md)
> describe the target ACR flow; do not publish this sample for APIM use.

The accepted distribution shape is one Hyperlight AOT file in one OCI layer. APIM
currently enforces an ACR URI, a tag or digest, exactly one layer, and the 64 MiB
limit. The prototype validator additionally enforces the canonical media types
listed above.

Use immutable manifest digests in `module-uri`. Tags are accepted by the current
product but stay cached for the gateway process lifetime, so changing a tag is not
a rolling update.

## Invoke a compatible module from Toolkit C#

After a future artifact passes every gate, reference it from the existing XML
authoring model:

```csharp
public void Inbound(IInboundContext context)
{
    context.RunWasmModule(new RunWasmModuleConfig
    {
        ModuleUri =
            "oci://contoso.azurecr.io/policies/auth-check@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    });
}
```

The compiler emits:

```xml
<run-wasm-module
    module-uri="oci://contoso.azurecr.io/policies/auth-check@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" />
```

The Toolkit requires a string literal and rejects a policy expression. APIM
validates that the full reference targets ACR and contains a tag or digest.

## Troubleshooting

| Message or result | Meaning | Action |
| --- | --- | --- |
| `.NET SDK '10.0.400' is required` | Another SDK was selected | Install the pinned SDK and run from the prototype directory so `global.json` applies |
| `.NET runtime '8.0.x' is required` | The unit-test host is missing | Install the .NET 8 runtime |
| `SHA-256 digest ... expected ...` | A tool archive or WIT file differs from the pin | Delete the download, verify the network source, and retry; do not bypass the check |
| `does not target the exact APIM policy world` | The component has missing or additional imports/exports | Inspect the captured WIT; today the expected cause is ambient WASI imports |
| `CompatibleWithCurrentApimHost : False` | Build/package succeeded but deployment compatibility did not | Stop before ACR publication |
| `The AOT component ... does not exist` | Component or AOT compilation did not complete | Review the preceding .NET or AOT error |
| `expected exactly one` | OCI manifest has the wrong number of layers | Repackage only the `.aot` file |
| `exceeds ... bytes` | The AOT layer is larger than APIM's 64 MiB limit | Reduce the artifact before publishing |
| AOT runtime-version error | Compiler and embedded Hyperlight/Wasmtime versions differ | Reinstall the pinned tool and rebuild; do not reuse stale AOT output |
| Module pull fails in APIM | Identity, role propagation, URI, or registry access is wrong | Verify the system-assigned identity, registry-scoped pull role, and immutable `oci://` reference |

## Advanced reference

<details>
<summary>Pinned toolchain</summary>

The exact values are in
[`eng/versions.json`](../prototype/wasm-policy/eng/versions.json). The current
prototype uses:

| Tool | Version |
| --- | --- |
| .NET SDK | `10.0.400` |
| .NET test runtime | `8.0` |
| Componentize.NET Wasm SDK | `0.8.0-preview00011` |
| NativeAOT-LLVM | `10.0.0-rc.1.26306.1` |
| Rust | `1.94.1` |
| `wasm-tools` | `1.256.0` |
| `hyperlight-wasm-aot` | `0.15.0` |
| ORAS | `1.3.0` |

Componentize.NET and NativeAOT-LLVM are prerelease dependencies from the public
`dotnet-experimental` feed. NuGet lock files record their exact package content
hashes.

</details>

<details>
<summary>Authoritative WIT contract</summary>

| Value | Contract |
| --- | --- |
| Package/world | `demo:policy@0.2.0` / `policy` |
| Host ABI | `3` |
| Normalized WIT SHA-256 | `d187a23aaa117fa8c21b29c9f09ac07cb6ac0eaa021820fe3e94d3b12c93ef6e` |
| Export | `demo:policy/handler@0.2.0` |
| Allowed imports | `demo:policy/api-inspector@0.2.0`, `demo:policy/types@0.2.0` |

The full WIT defines request/response URLs, headers, body, matched parameters,
API and operation metadata, product, subscription, deployment, user, workspace,
last-error, GraphQL, timing, variables, tracing, and allow/reject outcomes.

The current gateway meaningfully populates request method, current/original URL,
headers, body, client address, matched parameters, request ID, timestamp, elapsed
time, variables, and tracing. Treat other represented metadata as unavailable
until the product contract says otherwise.

</details>

<details>
<summary>Runtime behavior and limits</summary>

- APIM restores the Hyperlight snapshot after every invocation, including
  failures. Module globals do not persist.
- Calls are serialized per module sandbox.
- Execution is interrupted after two seconds or request cancellation.
- A gateway process caches at most 100 module references.
- The AOT layer may not exceed 64 MiB.
- Only trusted modules should be configured.

The APIM product prototype is limited to specifically enabled managed SKU v1
Windows Server 2022 x64 hosts with supported nested virtualization. It is not
generally available for Consumption, SKU v2 App Service, self-hosted gateway,
workspaces, AI Gateway, Linux, or Arm64.

</details>

## Next steps

- Follow the [customer quickstart](WasmPolicyQuickstart.md) for the target APIM and
  ACR journey.
- Read the
  [`AuthCheck` sample](../prototype/wasm-policy/samples/AuthCheck/AuthCheckPolicy.cs)
  and its
  [tests](../prototype/wasm-policy/test/AuthCheck.Tests/AuthCheckPolicyTests.cs).
- Keep the strict component-world gate in CI when extending the prototype.
- Wait for the runtime compatibility blocker to be resolved and proven under real
  Hyperlight before publishing a .NET module.
