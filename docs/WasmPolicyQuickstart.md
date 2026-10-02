# Try .NET policy modules for `run-wasm-module`

This quickstart shows the proposed customer experience for writing request
allow/reject logic in C#, testing it locally, packaging it for Azure API
Management, and referencing it from an APIM policy.

> [!CAUTION]
> This is an experimental preview in source form, not a deployable .NET module
> path. You can author, test, build, and inspect the sample today. The generated
> .NET component is rejected by the current APIM runtime compatibility check, so
> do not publish or configure it in a service. The ACR and APIM steps below show
> the target workflow and are clearly marked **contract only**.

For implementation details, API reference, and troubleshooting, see the
[.NET WASM policy module technical guide](WasmPolicyModules.md).

## What this enables

A .NET policy module receives a read-only view of the current request and returns
one of two outcomes:

- **Allow** — continue the APIM policy pipeline.
- **Reject** — stop normal processing and return an HTTP response through APIM's
  normal `on-error` flow.

The sample allows requests that contain a non-empty `Authorization` header and
rejects other requests with `401 Unauthorized`.

This is separate from the existing Policy Toolkit XML authoring model. Existing
methods such as `SetHeader`, `SendRequest`, and `CacheLookup` do not run inside the
module. The Toolkit only uses `RunWasmModule` to generate the XML element that
references a packaged module.

## Prerequisites

To try the local authoring flow:

- Windows x64 with PowerShell 7
- .NET SDK `10.0.400` and .NET runtime `8.0`
- Rust toolchain manager (`rustup`)
- this repository and branch

The provided setup script installs the pinned WASM, Hyperlight AOT, and ORAS tools
under the prototype directory after verifying their download hashes.

The target APIM deployment flow additionally requires:

- an Azure Container Registry
- an APIM service with a system-assigned managed identity
- APIM product-preview enablement on a supported managed SKU v1 Windows x64 host

The feature is not generally available or customer-enabled today.

## 1. Set up the sample

From the repository root:

```powershell
cd prototype\wasm-policy

rustup toolchain install 1.94.1 --profile minimal
.\eng\Install-Tools.ps1

$env:PATH = "$(Resolve-Path .\.tools\bin);$(Resolve-Path .\.tools\oras-1.3.0);$env:PATH"
$Oras = Resolve-Path .\.tools\oras-1.3.0\oras.exe
```

The sample contains:

```text
samples/AuthCheck/             C# policy logic
samples/AuthCheck.Component/   generated-WIT adapter and component project
test/AuthCheck.Tests/          ordinary .NET unit tests
```

No experimental package or project template is published. The prototype API is
referenced directly from `src/PolicyToolkit.Wasm.Experimental`.

## 2. Write allow/reject logic

The complete sample is
[`AuthCheckPolicy.cs`](../prototype/wasm-policy/samples/AuthCheck/AuthCheckPolicy.cs):

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

Keep module logic deterministic. The current contract does not provide outbound
HTTP, files, sockets, environment variables, host clocks, random data, named
values, or persistent state.

## 3. Unit test the policy

Run the policy as ordinary .NET code:

```powershell
dotnet test .\test\AuthCheck.Tests\AuthCheck.Tests.csproj --configuration Release
```

The sample tests allow, reject, case-insensitive header lookup, and empty-header
behavior. No Hyperlight host or Azure resource is needed.

## 4. Build and package locally

One command runs the tests, generates WIT bindings, builds the component, compiles
the Hyperlight AOT file, and creates a validated local OCI layout:

```powershell
.\eng\Build-Prototype.ps1 -OrasPath $Oras
```

Outputs are written under `artifacts`:

```text
artifacts/auth_check.component.wasm
artifacts/auth_check.aot
artifacts/auth-check-oci/
```

The command currently reports:

```text
CompatibleWithCurrentApimHost : False
```

That result is expected and is the reason this artifact must not be deployed.
A successful build proves packaging, not runtime compatibility.

## 5. Run the deployment-readiness gate

The strict validation command compares the component with the exact APIM policy
contract:

```powershell
.\eng\Test-ComponentContract.ps1 `
    -ComponentPath .\artifacts\auth_check.component.wasm
```

It currently fails because the .NET toolchain adds 18 WASI runtime imports that
APIM does not provide. Treat this command as the deployment gate: it must succeed,
and real Hyperlight allow/reject/reset tests must pass, before publishing a .NET
module for APIM.

The local build also validates that the OCI artifact has exactly one AOT layer,
uses the expected media types, stays within 64 MiB, and matches all declared
SHA-256 digests.

## 6. Push to ACR and capture a digest — contract only

> [!WARNING]
> Do not run this step with the current sample artifact. It documents the target
> customer workflow after the runtime blocker is resolved.

Authenticate without enabling the ACR admin account:

```powershell
$AcrName = "contoso"
$Registry = "$AcrName.azurecr.io"
$Repository = "policies/auth-check"
$Tag = "2026.10.02"

az login
az acr login --name $AcrName
```

Push the single AOT layer and capture its immutable manifest digest:

```powershell
Push-Location .\artifacts
try
{
    $Result = oras push "$Registry/$Repository`:$Tag" `
        "auth_check.aot:application/vnd.hyperlight.policy.v1+aot" `
        --artifact-type "application/vnd.hyperlight.policy.v1+aot" `
        --annotation "org.opencontainers.image.created=2026-10-02T00:00:00Z" `
        --format json | ConvertFrom-Json
}
finally
{
    Pop-Location
}

$ModuleUri = "oci://$Registry/$Repository@$($Result.digest)"
$ModuleUri
```

Save the printed digest reference. Do not use a mutable tag in an APIM policy:
tags remain cached for the gateway process lifetime and are not a rolling update
mechanism.

## 7. Configure APIM access — contract only

Enable the APIM system-assigned managed identity and grant pull access only to the
registry:

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

For an ABAC-enabled registry, grant `Container Registry Repository Reader`
instead of `AcrPull`. Do not enable anonymous pull or the registry admin account.

## 8. Invoke the module — contract only

In Policy Toolkit C#:

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

The Toolkit compiles it to:

```xml
<run-wasm-module
    module-uri="oci://contoso.azurecr.io/policies/auth-check@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" />
```

`module-uri` must be a literal ACR tag or digest reference. The Toolkit rejects
policy expressions; APIM validates the full URI.

## 9. Observe the result — target behavior

With the authorization sample:

- a request with `Authorization: Bearer example` returns **allow** and the next
  inbound policy runs;
- a request without the header returns **reject** with `401 Unauthorized` and
  `WWW-Authenticate: Bearer`;
- a reject or module error follows normal APIM `on-error` processing, so an
  `on-error` policy can replace the eventual response;
- APIM tracing records the module reference and the `allow` or `reject` outcome,
  not request data.

Module load, ACR pull, compatibility, timeout, and execution failures also enter
the policy runtime error path. They are not successful authorization decisions.

## 10. Update and roll back

1. Change and unit test the C# policy.
2. Build and pass the strict compatibility and real-runtime gates.
3. Push a new artifact and record its new digest.
4. Deploy a policy that references the new digest.
5. Keep the previous digest available until verification is complete.

Rollback by redeploying the previous digest. Do not repoint a tag and assume
running gateways will refresh it.

## Current limitations

- The generated .NET component is not compatible with the current APIM host.
- A restricted compatibility-adapter experiment removed the extra imports but
  faulted on its first real Hyperlight invocation.
- APIM `run-wasm-module` is a product prototype, not a generally available
  customer feature.
- The prototype supports local authoring on Windows x64; it does not publish a
  NuGet package or `dotnet new` template.
- Existing Policy Toolkit XML policies cannot be compiled into WASM modules.
- A module can inspect context and return allow/reject; it cannot call arbitrary
  APIM policies or mutate the request.

Continue with the
[technical guide](WasmPolicyModules.md) for the complete API surface, generated
bindings, validation gates, OCI contract, and troubleshooting.
