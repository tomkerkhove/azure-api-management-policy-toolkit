# Try .NET policy modules for `run-wasm-module`

This quickstart shows the proposed .NET customer experience for writing APIM
request allow/reject logic, testing it, producing a WebAssembly component, and
checking whether it matches the APIM contract.

> [!CAUTION]
> This is an experimental source sample, not a deployable .NET module path.
> The direct .NET build works, but its component imports runtime interfaces that
> APIM does not provide. The compatibility check in this guide therefore fails
> by design. Stop there. The later AOT, ACR, and APIM steps describe the target
> contract only.

For API details and troubleshooting, see the
[.NET WASM policy module technical guide](WasmPolicyModules.md).

## What this enables

A policy module receives a read-only request context and returns one outcome:

- **Allow** — continue the APIM policy pipeline.
- **Reject** — return an HTTP response through APIM's normal `on-error` flow.

The sample allows a request with a non-empty `Authorization` header and otherwise
returns `401 Unauthorized`.

This is separate from the existing Policy Toolkit XML DSL. Existing methods such
as `SetHeader` and `SendRequest` do not execute inside the module.

## Prerequisites

- Windows x64
- [.NET SDK `10.0.400`](https://dotnet.microsoft.com/download/dotnet/10.0)
- .NET runtime `8.0` for the sample tests
- PowerShell 7 for the copy/paste commands

The pinned Componentize.NET build does **not** require the
`wasi-experimental` workload. It was verified with no installed workloads.
Componentize.NET supplies the MSBuild and WIT-binding targets through NuGet.

The build uses these prerelease packages:

| Package | Version | Source |
| --- | --- | --- |
| `BytecodeAlliance.Componentize.DotNet.Wasm.SDK` | `0.8.0-preview00011` | NuGet.org |
| `runtime.win-x64.Microsoft.DotNet.ILCompiler.LLVM` | `10.0.0-rc.1.26306.1` | public `dotnet-experimental` feed |

The technologies are prerelease and intentionally isolated from the shipping
Policy Toolkit packages.

## 1. Get or create the sample

The runnable source sample is
[`prototype/wasm-policy`](../prototype/wasm-policy/). Download that folder as a
source archive or copy it into a working directory; a version-control checkout is not
required.

It contains:

```text
samples/AuthCheck/             policy logic
samples/AuthCheck.Component/   Componentize.NET project and WIT adapter
test/AuthCheck.Tests/          ordinary .NET tests
src/PolicyToolkit.Wasm.Experimental/
wit/policy.wit
NuGet.config
global.json
```

The sample already contains the source-only Toolkit API and policy projects. To
recreate its Componentize.NET project with standard .NET commands:

```powershell
dotnet new classlib `
    --name Contoso.AuthPolicy.Component `
    --framework net10.0

dotnet new nugetconfig
```

Replace the generated `NuGet.config` package sources with:

```xml
<packageSources>
  <clear />
  <add
      key="dotnet-experimental"
      value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/index.json" />
  <add
      key="nuget.org"
      value="https://api.nuget.org/v3/index.json" />
</packageSources>
```

Then add the exact packages:

```powershell
dotnet add .\Contoso.AuthPolicy.Component package `
    BytecodeAlliance.Componentize.DotNet.Wasm.SDK `
    --version 0.8.0-preview00011

dotnet add .\Contoso.AuthPolicy.Component package `
    runtime.win-x64.Microsoft.DotNet.ILCompiler.LLVM `
    --version 10.0.0-rc.1.26306.1
```

The experimental Toolkit API is not yet published as a NuGet package or template.
Use the checked-in source sample until a package is available.

## 2. Configure the component project

The component project uses ordinary SDK-style MSBuild:

```xml
<PropertyGroup>
  <OutputType>Library</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>
  <UseAppHost>false</UseAppHost>
  <PublishTrimmed>true</PublishTrimmed>
  <InvariantGlobalization>true</InvariantGlobalization>
  <SelfContained>true</SelfContained>
  <TargetName>auth_check</TargetName>
</PropertyGroup>

<ItemGroup>
  <PackageReference
      Include="BytecodeAlliance.Componentize.DotNet.Wasm.SDK"
      Version="0.8.0-preview00011" />
  <PackageReference
      Include="runtime.win-x64.microsoft.dotnet.ilcompiler.llvm"
      Version="10.0.0-rc.1.26306.1" />
</ItemGroup>

<ItemGroup>
  <Wit Remove="**/*.wit" />
  <Wit Include="../../wit" World="policy" />
  <LinkerArg Include="-Wl,--max-memory=67108864" />
</ItemGroup>
```

`policy.wit` is the authoritative APIM guest interface. Componentize.NET generates
the C# bindings during `dotnet build`; do not hand-copy those ABI types.

## 3. Write deterministic policy logic

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

The current contract does not provide outbound HTTP, files, sockets, environment
variables, host clocks, random data, named values, request mutation, or persistent
state.

## 4. Unit test with `dotnet test`

From the downloaded `wasm-policy` sample directory:

```powershell
dotnet restore .\WasmPolicyPrototype.slnx --locked-mode

dotnet test .\test\AuthCheck.Tests\AuthCheck.Tests.csproj `
    --configuration Release `
    --no-restore
```

The tests run as ordinary .NET and do not need Azure or Hyperlight.

## 5. Build the component with `dotnet build`

```powershell
dotnet build `
    .\samples\AuthCheck.Component\AuthCheck.Component.csproj `
    --configuration Release `
    --no-restore `
    --no-incremental
```

Componentize.NET generates bindings under:

```text
samples/AuthCheck.Component/obj/Release/net10.0/wasi-wasm/wit_bindgen/
```

The component output is:

```text
samples/AuthCheck.Component/bin/Release/net10.0/wasi-wasm/publish/auth_check.wasm
```

Use `dotnet build`, not `dotnet publish`, with this pinned preview SDK. Its build
targets already produce the published component.

## 6. Install the pinned `wasm-tools` binary

`wasm-tools` `1.256.0` has an official Windows x64 release archive. Download and
verify it directly:

```powershell
$Tools = Join-Path $PWD ".tools"
$WasmToolsZip = Join-Path $Tools "wasm-tools-1.256.0-x86_64-windows.zip"
$WasmToolsUrl = "https://github.com/bytecodealliance/wasm-tools/releases/download/v1.256.0/wasm-tools-1.256.0-x86_64-windows.zip"
$WasmToolsSha256 = "8ce28d91e40ac077a40e133d332d7909747f5179e795e3f022cfbad95e5df3b7"

New-Item -ItemType Directory -Path $Tools -Force | Out-Null
Invoke-WebRequest -Uri $WasmToolsUrl -OutFile $WasmToolsZip

$Actual = (Get-FileHash $WasmToolsZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Actual -ne $WasmToolsSha256)
{
    throw "wasm-tools archive SHA-256 mismatch."
}

Expand-Archive $WasmToolsZip -DestinationPath $Tools -Force
$WasmToolsDirectory = Join-Path $Tools "wasm-tools-1.256.0-x86_64-windows"
$env:PATH = "$WasmToolsDirectory;$env:PATH"
wasm-tools --version
```

## 7. Inspect and check compatibility

```powershell
$Component = ".\samples\AuthCheck.Component\bin\Release\net10.0\wasi-wasm\publish\auth_check.wasm"

wasm-tools validate $Component
wasm-tools component wit $Component
wasm-tools component targets -w policy .\wit $Component
```

The first two commands succeed. The final command currently fails and reports
missing `wasi:*` imports. This is the required stop point: the component does not
target the exact APIM world and is not deployable.

Do not treat a successful .NET build as runtime approval.

## 8. Hyperlight AOT — tooling blocked for customers

APIM loads a Hyperlight AOT file, not the portable `.wasm` component. The pinned
compiler command is:

```text
hyperlight-wasm-aot compile --component auth_check.wasm auth_check.aot
```

However, `hyperlight-wasm-aot` `0.15.0` does not have an official prebuilt Windows
release binary. The available distribution is a source package. This quickstart
does not ask customers to compile the tool. A trusted, pinned binary distribution
is required before AOT can be a standalone customer step.

Even with a trusted AOT compiler, the current component remains blocked by the
compatibility failure above and by the missing successful real-Hyperlight test.

## 9. Package and push with ORAS — target contract only

> [!WARNING]
> Do not publish the current sample. These commands describe the target workflow
> after the component and AOT gates pass.

Install the official ORAS `1.3.0` Windows x64 binary:

```powershell
$OrasZip = Join-Path $Tools "oras_1.3.0_windows_amd64.zip"
$OrasUrl = "https://github.com/oras-project/oras/releases/download/v1.3.0/oras_1.3.0_windows_amd64.zip"
$OrasSha256 = "b050e93aa0dc7a79a61fa8e4074dfa302c41d4af01b634fe393c5dd687536aee"

Invoke-WebRequest -Uri $OrasUrl -OutFile $OrasZip
$Actual = (Get-FileHash $OrasZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Actual -ne $OrasSha256)
{
    throw "ORAS archive SHA-256 mismatch."
}

$OrasDirectory = Join-Path $Tools "oras-1.3.0"
Expand-Archive $OrasZip -DestinationPath $OrasDirectory -Force
$env:PATH = "$OrasDirectory;$env:PATH"
oras version
```

After a trusted AOT compiler produces `auth_check.aot`, create a local OCI layout:

```powershell
oras push --oci-layout ".\artifacts\auth-check-oci:v1" `
    ".\artifacts\auth_check.aot:application/vnd.hyperlight.policy.v1+aot" `
    --artifact-type "application/vnd.hyperlight.policy.v1+aot" `
    --annotation "org.opencontainers.image.created=2026-10-02T00:00:00Z"

oras manifest fetch --oci-layout ".\artifacts\auth-check-oci:v1"
```

The APIM contract requires exactly one AOT layer no larger than 64 MiB.

To push to ACR and capture the immutable digest:

```powershell
$AcrName = "contoso"
$Registry = "$AcrName.azurecr.io"
$Repository = "policies/auth-check"
$Tag = "2026.10.02"

az login
az acr login --name $AcrName

$Result = oras push "$Registry/$Repository`:$Tag" `
    ".\artifacts\auth_check.aot:application/vnd.hyperlight.policy.v1+aot" `
    --artifact-type "application/vnd.hyperlight.policy.v1+aot" `
    --annotation "org.opencontainers.image.created=2026-10-02T00:00:00Z" `
    --format json | ConvertFrom-Json

$ModuleUri = "oci://$Registry/$Repository@$($Result.digest)"
$ModuleUri
```

Use the digest reference, not a mutable tag.

## 10. Configure and invoke APIM — target contract only

Enable the APIM system-assigned identity and grant registry-scoped pull access:

```powershell
$ResourceGroup = "contoso-api-rg"
$ApimName = "contoso-apim"

az apim update `
    --resource-group $ResourceGroup `
    --name $ApimName `
    --set identity.type=SystemAssigned

$PrincipalId = az apim show `
    --resource-group $ResourceGroup `
    --name $ApimName `
    --query identity.principalId `
    --output tsv

$AcrId = az acr show --name $AcrName --query id --output tsv

az role assignment create `
    --assignee-object-id $PrincipalId `
    --assignee-principal-type ServicePrincipal `
    --role AcrPull `
    --scope $AcrId
```

Reference the immutable module from Policy Toolkit C#:

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

An allow result continues the inbound pipeline. A rejection or module error enters
normal `on-error` processing.

For an update, publish a new digest and change the policy reference. Roll back by
redeploying the previous digest. Repointing a tag is not a rolling update.

## Current limitations

- The direct .NET component has 18 ambient WASI imports and fails the exact APIM
  world check.
- No real-Hyperlight .NET allow/reject/reset test passes today.
- The pinned AOT compiler lacks an official prebuilt customer binary.
- The experimental Toolkit API is source-only; no NuGet package or template is
  published.
- `run-wasm-module` is an APIM product prototype, not a generally available
  customer feature.

Continue with the
[technical guide](WasmPolicyModules.md) for API details, validation gates, and
maintainer automation.
