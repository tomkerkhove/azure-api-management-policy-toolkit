# Author .NET modules for `run-wasm-module` (experimental)

This guide defines the proposed Policy Toolkit journey for writing a deterministic
request-inspection module in C#, compiling it against API Management's WebAssembly
Interface Type (WIT) contract, packaging the Hyperlight AOT output as an OCI
artifact, publishing it to Azure Container Registry (ACR), and invoking it from an
API Management policy.

> [!CAUTION]
> This is a source-only prototype, not a supported end-to-end .NET deployment path.
> The sample builds, generates WIT bindings, passes ordinary .NET tests, validates
> as a WebAssembly component, compiles to Hyperlight AOT, and produces the current
> APIM OCI layout. It does **not** run in the current APIM Hyperlight host because
> Componentize.NET adds ambient WASI Preview 2 imports that the host intentionally
> does not provide. Do not publish or configure the sample in a service.

## What is implemented

| Area | Status | Evidence |
| --- | --- | --- |
| Deterministic C# policy API and `Authorization` sample | Working | `prototype/wasm-policy/src`, `samples/AuthCheck`, and four local unit tests |
| Authoritative WIT and generated C# bindings | Working | `prototype/wasm-policy/wit/policy.wit`; bindings are generated in `obj`, not copied by hand |
| Componentize.NET component build | Experimental | Builds with the pinned prerelease .NET 10 toolchain |
| WIT export validation | Working | Exports `demo:policy/handler@0.2.0` |
| APIM import allowlist | Blocked | The component also imports 18 `wasi:*@0.2.6` interfaces |
| Hyperlight AOT compilation | Experimental | Pinned `hyperlight-wasm-aot` produces an x64 `.aot` file |
| APIM-shaped OCI layout and digest checks | Working | Local validation checks media types, one layer, size, and blob digest |
| Real Windows Hypervisor Platform execution | Blocked | The uncomposed component cannot link; a restricted composed .NET experiment faulted on its first real invocation |
| ACR publication and APIM invocation | Contract documented only | Unsafe until the import/runtime blocker is resolved and real execution passes |

The prototype deliberately has two separate programming models:

1. The existing Policy Toolkit API still authors APIM XML policy documents. The
   `RunWasmModule` method added by this prototype only emits the invocation element.
2. `Microsoft.Azure.ApiManagement.PolicyToolkit.Wasm` is a small, separate API for
   logic that executes inside a guest. Existing policy methods such as `SendRequest`,
   `SetHeader`, or `CacheLookup` do not become executable guest code.

### How this fits the Policy Toolkit

The supported Toolkit uses Roslyn to read C# policy-document syntax and build an
APIM XML document. It does not compile that C# into executable gateway logic. Its
testing package emulates policy handlers and evaluates ordinary expression helpers
locally, while the deployed result remains XML plus APIM policy expressions.

The WASM prototype adds a parallel executable path:

| Input | Build path | Deployed result |
| --- | --- | --- |
| Existing `IDocument` / section methods | Toolkit compiler CLI and method-policy handlers | APIM XML policy document |
| Experimental `IWasmPolicyModule` | Componentize.NET, generated WIT bindings, then Hyperlight AOT | One executable AOT layer in ACR |

`RunWasmModuleConfig` belongs to the first row: it emits the XML that references an
artifact. `AuthCheckPolicy` belongs to the second row: it is compiled into the
artifact. There is no conversion between arbitrary `IDocument` code and a guest
module.

## Current product contract

The checked-in [`policy.wit`](../prototype/wasm-policy/wit/policy.wit) was copied
from merged APIM product source. Its
[`provenance.json`](../prototype/wasm-policy/wit/provenance.json) records the
source commit and path.

| Contract value | Current value |
| --- | --- |
| WIT package/world | `demo:policy@0.2.0` / `policy` |
| Host ABI | `3` |
| Normalized WIT SHA-256 | `d187a23aaa117fa8c21b29c9f09ac07cb6ac0eaa021820fe3e94d3b12c93ef6e` |
| Guest export | `handler.on-request(context: context) -> decision` |
| Host import | `api-inspector.log(message: string, source: string)` |

The world imports `api-inspector` and exports `handler`. The generated component
also represents `types` as a versioned import because those types cross the exported
handler boundary. A compatible component may therefore import only:

```text
demo:policy/api-inspector@0.2.0
demo:policy/types@0.2.0
```

`api-inspector.log` is the only host action. The guest cannot make outbound HTTP
requests, read named values, open files or sockets, inspect environment variables,
obtain random data, use host clocks, mutate the APIM request, or persist policy
variables.

### WIT types

The exact record definitions remain in the single WIT file. The model contains:

- `string-values { name, values }` for case-insensitive APIM dictionaries with
  every value preserved.
- `string-property { name, value }` for scalar property dictionaries.
- `duration { ticks }`, `foundry { deployment }`, and
  `url { scheme, host, port, path, query-string, query }`.
- `request { body?, foundry, headers, ip-address, method, matched-parameters,
  original-url, url }`.
- `response { body, headers, status-code, status-reason }`.
- API metadata: `subscription-key-parameter-names`, `api`, and `operation`.
- Product and identity metadata: `product-state`, `group`, `product`,
  `subscription`, `user-identity`, and `user`.
- Deployment metadata: `gateway`, `carbon-intensity-category`,
  `sustainability-info`, and `deployment`.
- `workspace`, `last-error`, and `graphql`.
- `context { request, response, api, operation, product, subscription, deployment,
  request-id, timestamp, elapsed, variables, user, workspace, tracing, last-error,
  graphql }`.
- `decision`, with exactly two cases: `allow` and `reject(response)`.

The current gateway meaningfully populates request method, current/original URL,
headers, body, client address, matched parameters, request ID, timestamp, elapsed
time, variables, and tracing. Other resource metadata represented by the WIT is
currently supplied as empty/default values. Treat it as unavailable until the
product contract explicitly changes.

An `allow` decision continues the existing policy pipeline. A `reject(response)`
decision fails the pipeline with the returned status, reason, headers, and binary
body, then follows normal `on-error` processing. Status codes outside `100-599` are
rejected. APIM removes hop-by-hop response headers and `Content-Length`; it handles
`Content-Type` separately.

There is no guest-visible error result. A trap, timeout, cancellation, load failure,
or snapshot-restoration failure becomes a normal APIM policy runtime error. The
gateway does not expose the native diagnostic or customer request data to the
client.

### Isolation and resource limits

- APIM restores the Hyperlight snapshot after every call, including rejected and
  failed calls. Module globals do not persist between requests.
- Calls are asynchronous across modules but serialized per module sandbox.
- Execution is interrupted after two seconds or request cancellation.
- A gateway process caches at most 100 module URIs across inherited policy scopes.
- The downloaded OCI layer may not exceed 64 MiB.
- The current native sandbox uses a 64 MiB input buffer, 16 MiB heap, and 65 MiB
  scratch region.
- Only trusted modules should be configured. Isolation limits impact, but does not
  turn arbitrary guest code into trusted code.

## Prototype layout

```text
prototype/wasm-policy/
├── eng/                         pinned tool, build, WIT, and OCI validation
├── samples/AuthCheck/           ordinary testable C# logic
├── samples/AuthCheck.Component/ generated-WIT adapter and component project
├── src/PolicyToolkit.Wasm.Experimental/
├── test/AuthCheck.Tests/
└── wit/                         one authoritative policy.wit plus provenance
```

No experimental project is included in the repository's shipping solution or
NuGet packages. That prevents a preview Componentize.NET dependency from leaking
into the supported Policy Toolkit build.

## Prerequisites

The versions are centralized in
[`eng/versions.json`](../prototype/wasm-policy/eng/versions.json).

| Tool | Pinned version |
| --- | --- |
| .NET SDK | `10.0.400` |
| .NET runtime used by ordinary policy tests | `8.0` |
| .NET workload | None; Componentize.NET supplies the component build targets |
| Componentize.NET Wasm SDK | `0.8.0-preview00011` |
| NativeAOT-LLVM | `10.0.0-rc.1.26306.1` |
| Rust toolchain | `1.94.1` |
| `wasm-tools` | `1.256.0` |
| `hyperlight-wasm-aot` | `0.15.0` |
| Hyperlight host embedded by the product prototype | `0.17.0` |
| ORAS | `1.3.0` |

Componentize.NET and NativeAOT-LLVM are prerelease dependencies from the public
`dotnet-experimental` feed. They are intentionally isolated under
`prototype/wasm-policy/NuGet.config`, and NuGet lock files record exact package
content hashes.

### Windows PowerShell

Use PowerShell 7 on Windows x64:

```powershell
cd prototype\wasm-policy

rustup toolchain install 1.94.1 --profile minimal
.\eng\Install-Tools.ps1

$env:PATH = "$(Resolve-Path .\.tools\bin);$(Resolve-Path .\.tools\oras-1.3.0);$env:PATH"
```

The installer downloads the exact `wasm-tools` and `hyperlight-wasm-aot` crate
archives and the ORAS Windows archive, verifies their SHA-256 digests, and builds
the Cargo tools with the published lock files. It does not install an Azure
credential or enable any Windows feature.

### Bash

The build and validation scripts use PowerShell 7. The hash-verified bootstrap
script currently supports Windows x64 only. The component project also includes a
Linux x64 NativeAOT package; macOS is not supported. On Linux x64, install the same
pinned Rust toolchain, verify the crate hashes from `eng/versions.json`, and
install from the verified archives. Install ORAS `1.3.0` from its official release
and verify the platform-specific checksum.

```bash
cd prototype/wasm-policy
rustup toolchain install 1.94.1 --profile minimal

mkdir -p .tools/cache .tools/src
curl -fsSL https://crates.io/api/v1/crates/wasm-tools/1.256.0/download \
  -o .tools/cache/wasm-tools-1.256.0.crate
echo "648fb01b39cb01354037807cdd4ae4e384590a1bdb727018bc12b68fb03ed385  .tools/cache/wasm-tools-1.256.0.crate" \
  | sha256sum --check
tar -xzf .tools/cache/wasm-tools-1.256.0.crate -C .tools/src
cargo +1.94.1 install --locked --path .tools/src/wasm-tools-1.256.0 --root .tools

curl -fsSL https://crates.io/api/v1/crates/hyperlight-wasm-aot/0.15.0/download \
  -o .tools/cache/hyperlight-wasm-aot-0.15.0.crate
echo "b183b0031abe0dca7127bed2fd741640ac1716ae30a810b60a93c2b468846cb8  .tools/cache/hyperlight-wasm-aot-0.15.0.crate" \
  | sha256sum --check
tar -xzf .tools/cache/hyperlight-wasm-aot-0.15.0.crate -C .tools/src
cargo +1.94.1 install --locked \
  --path .tools/src/hyperlight-wasm-aot-0.15.0 \
  --root .tools

export PATH="$PWD/.tools/bin:$PATH"
```

## Author and test a module

The sample keeps business logic independent from generated ABI types:

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
                401,
                "Unauthorized",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["WWW-Authenticate"] = new[] { "Bearer" }
                }));
    }
}
```

Run it as ordinary .NET code:

```powershell
dotnet test .\test\AuthCheck.Tests\AuthCheck.Tests.csproj --configuration Release
```

The Componentize.NET project references the same policy class. Its
`HandlerExportsImpl` is only an adapter between generated WIT types and the small
Toolkit API. Do not check generated bindings into source or reproduce the WIT
records manually.

## Build and inspect the component

On Windows, run the complete local flow:

```powershell
$Oras = Resolve-Path .\.tools\oras-1.3.0\oras.exe
.\eng\Build-Prototype.ps1 -OrasPath $Oras
```

The script:

1. restores locked NuGet dependencies;
2. runs the ordinary C# tests;
3. generates WIT bindings and builds `auth_check.component.wasm`;
4. runs `wasm-tools validate` and records `wasm-tools component wit`;
5. compiles `auth_check.aot` with the pinned Hyperlight AOT compiler;
6. creates and validates a local single-layer OCI image layout.

Artifacts are written under `prototype/wasm-policy/artifacts` and ignored by Git.

Run the strict APIM import gate separately:

```powershell
.\eng\Test-ComponentContract.ps1 `
  -ComponentPath .\artifacts\auth_check.component.wasm
```

It currently fails by design. The generated component imports 18 WASI Preview 2
interfaces in the `wasi:io`, `wasi:clocks`, `wasi:cli`, `wasi:filesystem`, and
`wasi:random` packages. APIM exposes only the two `demo:policy` interfaces shown
earlier.

`hyperlight-wasm-aot` can compile that component, but successful AOT compilation
proves neither host compatibility nor safe execution.

### Exact runtime blocker

A deny-by-default WASI-Virt `0.2.0` composition experiment at commit
`448f6df8f688cee5d6995e96b1ffc31f9bf00742` (using its pinned
`nightly-2026-07-27` toolchain) removed all ambient WASI imports and left only the
APIM interfaces. The default adapter introduced an unbounded memory and exhausted
the Hyperlight sandbox. After both guest and adapter memories were bounded, the
component loaded under real Windows Hypervisor Platform but its first `on-request`
call trapped with a native general-protection fault. Increasing memory did not fix
the ABI fault. WASI-Virt is therefore not included in this prototype build.

The path remains blocked until the upstream .NET component toolchain can emit a
component with no ambient WASI imports, or a bounded adapter is proven compatible
with the pinned Hyperlight runtime through allow, reject, timeout, and snapshot
reset tests.

## OCI artifact contract

The merged APIM reference producer uses this layout:

| Descriptor | Media type |
| --- | --- |
| Manifest | `application/vnd.oci.image.manifest.v1+json` |
| `artifactType` | `application/vnd.hyperlight.policy.v1+aot` |
| Empty config | `application/vnd.oci.empty.v1+json` |
| Single AOT layer | `application/vnd.hyperlight.policy.v1+aot` |

The layer filename is not significant to the current gateway, but the reference
producer uses `auth_check.aot`. The local validator requires exactly one layer,
checks its descriptor size is at most `67,108,864` bytes, recalculates its SHA-256
digest, and validates the manifest digest returned by ORAS.

The current gateway itself enforces an ACR URI, a tag or digest, exactly one layer,
and the 64 MiB limit. It currently does not reject an artifact based on artifact,
config, or layer media type. It also does not consume OCI architecture, ABI, or WIT
annotations. ABI `3` and the WIT digest are validated between the gateway and its
packaged native runtime, while the guest's actual WIT imports/exports are validated
when the component is loaded. Keep the canonical media types above rather than
depending on today's permissive parser.

The AOT output is architecture- and runtime-specific. This prototype compiler
targets `x86_64-unknown-none`, and its version must match the Hyperlight/Wasmtime
runtime used by APIM. An ordinary .NET assembly, the portable `.wasm` component,
or AOT output from a different compiler version is not an executable APIM module.

## Publish to ACR

> [!WARNING]
> The commands in this section describe the product contract. Do not push this
> prototype's incompatible artifact for APIM use.

Authenticate interactively without enabling the ACR admin account or placing a
password in shell history:

```powershell
$AcrName = "contoso"
$Registry = "$AcrName.azurecr.io"
$Repository = "policies/auth-check"
$Tag = "2026.10.01"

az login
az acr login --name $AcrName
```

Push the one AOT layer and capture the immutable digest:

```powershell
Push-Location .\artifacts
try
{
    $Result = oras push "$Registry/$Repository`:$Tag" `
        "auth_check.aot:application/vnd.hyperlight.policy.v1+aot" `
        --artifact-type "application/vnd.hyperlight.policy.v1+aot" `
        --annotation "org.opencontainers.image.created=2026-10-01T00:00:00Z" `
        --format json | ConvertFrom-Json
}
finally
{
    Pop-Location
}

$Digest = $Result.digest
$ModuleUri = "oci://$Registry/$Repository@$Digest"
$ModuleUri
```

The Bash equivalent uses line continuations but the same media types:

```bash
ACR_NAME=contoso
REGISTRY="$ACR_NAME.azurecr.io"
REPOSITORY=policies/auth-check
TAG=2026.10.01

az login
az acr login --name "$ACR_NAME"

cd artifacts
DIGEST="$(
  oras push "$REGISTRY/$REPOSITORY:$TAG" \
    auth_check.aot:application/vnd.hyperlight.policy.v1+aot \
    --artifact-type application/vnd.hyperlight.policy.v1+aot \
    --annotation org.opencontainers.image.created=2026-10-01T00:00:00Z \
    --format json | jq -r .digest
)"
MODULE_URI="oci://$REGISTRY/$REPOSITORY@$DIGEST"
printf '%s\n' "$MODULE_URI"
```

Tags are accepted by the current product, but a tag is resolved once and remains
cached for the gateway process lifetime. Repointing a tag is not a rolling update.
Persist the `oci://...@sha256:...` value printed above.

## Configure APIM access

The current gateway uses the API Management service's **system-assigned** managed
identity to pull from ACR. Enable it and grant the least-privileged registry-scoped
role:

```powershell
$ResourceGroup = "contoso-api-rg"
$ApimName = "contoso-apim"
$AcrName = "contoso"

az apim update `
    --resource-group $ResourceGroup `
    --name $ApimName `
    --set identity.type=SystemAssigned

$PrincipalId = az apim show `
    --resource-group $ResourceGroup `
    --name $ApimName `
    --query identity.principalId `
    --output tsv

$AcrId = az acr show `
    --name $AcrName `
    --query id `
    --output tsv

az role assignment create `
    --assignee-object-id $PrincipalId `
    --assignee-principal-type ServicePrincipal `
    --role AcrPull `
    --scope $AcrId
```

Use `Container Registry Repository Reader` instead of `AcrPull` for an
[ABAC-enabled registry](https://learn.microsoft.com/azure/container-registry/container-registry-rbac-abac-repository-permissions).
Allow time for role-assignment propagation. Do not enable anonymous pull or the ACR
admin account.

The product prototype is not generally available. It is limited to managed SKU v1
Gateway V1/V2 on Windows Server 2022 x64 with nested virtualization and Windows
Hypervisor Platform. The current VM allowlist is Dv3, Dsv3, Dadsv6, and Ddsv6 in
the APIM D2-D64 size inventory. Dav4, Dsv4, Consumption, SKU v2 App Service,
self-hosted gateway, workspaces, AI Gateway, Linux, and Arm64 are unsupported.
Availability also requires APIM product enablement; it is not a customer-controlled
policy switch.

## Invoke the module

The existing Toolkit authoring model emits XML; it does not embed or compile the
guest:

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

The compiled element is:

```xml
<run-wasm-module
    module-uri="oci://contoso.azurecr.io/policies/auth-check@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" />
```

`module-uri` must be a literal ACR reference. Policy expressions, credentials,
ports, query strings, fragments, and non-ACR registries are rejected. The Toolkit
compiler intentionally emits the literal and leaves full URI validation to APIM so
there is one product parser.

For an `allow` result, the next inbound policy runs. A `reject` result enters normal
`on-error` handling with reason `WasmModuleRejected`; an `on-error` policy may
replace the eventual response. Load, pull, compatibility, timeout, and execution
failures also enter `on-error` through the generic policy runtime failure path.
Tracing records the module URI and `allow`/`reject` outcome, not request data.

## Update and rollback

1. Change and unit-test the C# logic.
2. Rebuild with the exact pinned toolchain.
3. Require the strict import allowlist and real Hyperlight allow/reject/reset tests
   to pass before promotion.
4. Publish a new artifact and record its manifest digest.
5. Change the policy to the new digest and deploy it.
6. Retain the previous digest until the rollout is verified.

Rollback means redeploying the previous digest. Do not overwrite a tag and assume
gateways will refresh it, and do not delete the previous manifest while policies
still reference it.

## Required work before a supported .NET journey

1. Resolve the Componentize.NET/WASI/Hyperlight first-call fault without expanding
   ambient host capabilities.
2. Add a real Windows Server 2022 WHP compatibility test that proves allow, reject,
   timeout interruption, recovery, snapshot reset, and disposal.
3. Turn the experimental source project into a versioned Toolkit package/template
   only after the toolchain is supportable.
4. Define and enforce guest architecture, ABI, WIT digest, provenance, and signature
   metadata in the product contract instead of treating them as advisory OCI
   annotations.
5. Publish customer-facing APIM availability and support documentation before
   presenting the ACR steps as generally usable.

## References

- [Policy Toolkit WASM prototype](../prototype/wasm-policy/README.md)
- [Hyperlight APIM .NET design reference](https://github.com/tomkerkhove/hyperlight-apim/blob/main/docs/dotnet-policy.md)
- [Use managed identities in Azure API Management](https://learn.microsoft.com/azure/api-management/api-management-howto-use-managed-service-identity)
- [Use a managed identity to authenticate to ACR](https://learn.microsoft.com/azure/container-registry/container-registry-authentication-managed-identity)
- [Manage OCI artifacts in ACR with ORAS](https://learn.microsoft.com/azure/container-registry/container-registry-manage-artifact)

The Hyperlight APIM repository is a design and proof-of-concept reference. Its demo
gateway, portable-component promotion flow, and older layer conventions are not the
current APIM product contract.
