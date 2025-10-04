# Methodic Acetone – Service Fabric URL Resolver for IIS

Acetone is an IIS rewrite provider that discovers Service Fabric services on the fly, resolves partitions, and caches endpoints so that applications never hard-code cluster URLs. The core rewrite provider targets **.NET Framework 4.8.1** while the sample/stateless Service Fabric services in this repository target **.NET 9** to demonstrate modern hosting side-by-side.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET Framework 4.8.1](https://img.shields.io/badge/.NET-4.8.1-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework)
[![Service Fabric](https://img.shields.io/badge/Service%20Fabric-8.2.274-purple.svg)](https://learn.microsoft.com/azure/service-fabric/)

---

## Why Acetone?

- **Automatic discovery** – resolve Service Fabric applications / services and partitions with no manual wiring.
- **Production-savvy caching** – warm and reuse cluster metadata (applications, services, partitions) for fast rewrites.
- **Pull-request routing** – map URLs like `https://guard-12906.example.com` to the Service Fabric application `Guard-PR12906` automatically (first letter capitalised, rest lower-cased).
- **IPv6 + normalization aware** – parses endpoints with IPv4, bracketed IPv6 (e.g. `https://[::1]:8080`), converts `0.0.0.0` → `127.0.0.1` and `[::]` → `[::1]` for local routability.
- **Secure connectivity** – client thumbprint or common-name + issuer Distinguished Name authentication, optional remote server validation.
- **Robust error semantics** – distinct exceptions for null, empty, invalid URL inputs; clear diagnostics for missing cluster settings.
- **Well tested** – extensive unit + mock integration suites; real cluster optional.

---

## Quick Start

1. **Restore & build**
   ```powershell
   nuget restore acetone.sln
   msbuild acetone.sln /p:Configuration=Release
   ```
2. **Copy** `Methodic.Acetone.dll` to the IIS server (e.g. into your site `bin`).
3. **Configure** the provider in `web.config` (see minimal config below).
4. **Deploy your SF apps** (e.g. `Guard`, `Guard-PR12906`).
5. **Add a rewrite rule** that proxies to the resolved endpoint.

### Minimal provider configuration

```xml
<rewrite>
  <providers>
    <provider name="ServiceFabric" type="Methodic.Acetone.ServiceFabricLocator, Methodic.Acetone">
      <settings>
        <add key="ClusterConnectionStrings" value="localhost:19000" />
        <add key="ApplicationNameLocation" value="Subdomain" />
        <add key="EnableLogging" value="true" />
        <add key="CredentialsType" value="Local" />
      </settings>
    </provider>
  </providers>
</rewrite>
```

---

## Configuration Cheat Sheet

| Setting | Purpose | Notes |
|---------|---------|-------|
| `ClusterConnectionStrings` | One or more comma-separated Service Fabric gateways. | REQUIRED; empty or whitespace rejected. Example: `node1:19000,node2:19000`. |
| `ApplicationNameLocation` | Where to derive the application name. | `Subdomain` (default), `SubdomainPreHyphens`, `SubdomainPostHyphens`, `FirstUrlFragment`. |
| `PartitionCacheLimit` | Fabric client partition location cache size. | Default `5` (set before client creation). |
| `EnableLogging` | Emit informational + debug/ warning events. | Boolean; default `false`. |
| `CredentialsType` | Cluster authentication mode. | `Local`, `CertificateThumbprint`, `CertificateCommonName`. |
| `ClientCertificateThumbprint` | Local client cert thumbprint. | Required for `CertificateThumbprint`. Stored in `LocalMachine\My`. |
| `ServerCertificateThumbprints` | Remote gateway/server cert thumbprints. | Optional extra validation (comma-separated). |
| `ClientCertificateSubjectDistinguishedName` | Client cert Subject DN. | Required for `CertificateCommonName`. |
| `ClientCertificateIssuerDistinguishedName` | Client cert Issuer DN. | Required for `CertificateCommonName`. |
| `ServerCertificateCommonNames` | Accepted remote common names (CN/SAN). | Optional; comma-separated. |
| `VersionParameter` | (Reserved) Version query string key. | Future feature. |
| `ClearCacheParameter` | (Reserved) Cache-bypass query key. | Future feature. |

### Pull‑request aware routing

Active in `Subdomain` & `FirstUrlFragment` modes. Pattern: `{serviceName}-{digits}` → `{ServiceName}-PR{digits}`. Only first character is capitalised; the rest lower‑cased (e.g. `API-1234` → `Api-PR1234`).

### Endpoint parsing & normalization

- Accepts plain HTTP/S URLs or JSON of the form: `{ "Endpoints": { "": "https://host:port" } }` or named keys like `HttpListener`.
- Extracts the first HTTP/HTTPS endpoint, ignoring remoting endpoints.
- Supports bracketed IPv6: `https://[2001:db8::1]:8443`.
- Rewrites non‑routable addresses:
  - `0.0.0.0` → `127.0.0.1`
  - `[::]` → `[::1]`

---

## Example rewrite rule

```xml
<rewrite>
  <rules>
    <rule name="ReverseProxy" stopProcessing="true">
      <match url="(.*)" />
      <action type="Rewrite" url="{C:1}://{SERVER_NAME}:{C:3}{URL}" appendQueryString="true" />
      <conditions>
        <add input="{ACETONE:{CACHE_URL}}" pattern="(.+):\\/\\/(.+):(\d+)" />
      </conditions>
    </rule>
  </rules>
</rewrite>
```

(Adjust action target if you need host substitution or SSL offload.)

---

## Caching Model

| Cache | Scope | Backing Type | Invalidation | Notes |
|-------|-------|--------------|--------------|-------|
| Applications | Process | ConcurrentDictionary (lazy) | Manual additions / app lookups | Keyed by `APPNAME[-version]`. |
| Services | Process | ConcurrentDictionary (lazy) | Service Fabric notifications | Service kind heuristic: first stateless *API* or *Service* name (or *FUNCTION* for function mode). |
| Partitions | Process | ConcurrentDictionary | TTL (30s) or env var disable | TTL sliding window; disable with `ACETONE_DISABLE_PARTITION_CACHE=1`. |

Warmup attempts to prime caches but fails gracefully if cluster not reachable.

---

## Testing & Verification

```powershell
# Default (unit + integration using mock cluster if real cluster absent)
dotnet test

# Unit only
dotnet test --filter "TestCategory=Unit"

# Integration (mock + real when cluster present)
dotnet test --filter "TestCategory=Integration"
```

Environment variables:
- `ACETONE_SKIP_DEPLOY=1` – Skip real cluster deployment (forces mock resolver).
- `ACETONE_SKIP_APP_TYPE_UNPROVISION=1` – Preserve provisioned app types after tests.
- `ACETONE_DISABLE_PARTITION_CACHE=1` – Bypass partition caching (diagnostics / perf stress).

---

## Development Workflow

- Restore: `nuget restore acetone.sln`
- Build (Debug): `msbuild acetone.sln /p:Configuration=Debug`
- Test: `dotnet test`
- Explore logs: enable `EnableLogging=true` in provider settings.

---

## Architecture Snapshot

- `ServiceFabricUrlParser` – URL → application name / endpoint extraction.
- `ServiceFabricUrlResolver` – cluster querying, caches, partition resolution.
- `ServiceFabricLocator` – IIS rewrite provider entrypoint & validation.
- `MockServiceFabricUrlResolver` / `TestableServiceFabricUrlResolver` – deterministic in‑memory test doubles.
- `ServiceFabricTestClusterManager` – optional local cluster orchestration for integration tests.

---

## Example Usage

```csharp
using Methodic.Acetone;

var logger = new TraceLogger { Enabled = true };
using var resolver = new ServiceFabricUrlResolver(logger, "localhost:19000");

if (ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
        "https://guard-12906.example.com",
        ApplicationNameLocation.Subdomain,
        out var appName))
{
    var endpoint = await resolver.ResolveServiceUri(appName, Guid.NewGuid());
    Console.WriteLine($"Resolved endpoint: {endpoint}");
}
```

---

## Reference Guide

### URL Parsing Modes

| Mode | Example | Extracted |
|------|---------|-----------|
| Subdomain | `https://myservice.company.com` | `myservice` |
| SubdomainPreHyphens | `https://myservice-uat-01.company.com` | `myservice` |
| SubdomainPostHyphens | `https://uat-01-myservice.company.com` | `myservice` |
| FirstUrlFragment | `https://api.company.com/myservice/v1` | `myservice` |

### Pull Request Pattern Examples

| Input Portion | Output | Notes |
|---------------|--------|-------|
| `guard-12906` | `Guard-PR12906` | Only first char capitalised |
| `API-1234` | `Api-PR1234` | Rest lower‑cased |
| `service-999` | `Service-PR999` | Any length digits |
| `my-service` | `my-service` | Not PR (no terminal digits) |

### Error Semantics

| Scenario | Exception |
|----------|-----------|
| Rewrite null URL | `ArgumentNullException` |
| Rewrite empty / whitespace URL | `ArgumentException` |
| Invalid single label (e.g. `foo`) | `ArgumentException` |
| Unresolvable application (valid pattern but not deployed) | `KeyNotFoundException` |

### Performance Tips

1. Reuse a single resolver instance per worker process.
2. Place lowest‑latency gateway first in `ClusterConnectionStrings`.
3. Increase `PartitionCacheLimit` only if you observe churn > limit.
4. Leave `EnableLogging=false` in steady state; enable temporarily for diagnostics.
5. Warm critical application names on startup (first resolution primes all caches).

### Common Issues

| Symptom | Likely Cause | Action |
|---------|--------------|--------|
| `ArgumentException` on rewrite | Empty / malformed incoming URL | Validate rewrite rule substitution variables. |
| `KeyNotFoundException` resolving | App not deployed or wrong name | Check SF Explorer; confirm naming pattern. |
| Endpoint shows `0.0.0.0` | Non‑routable binding | Automatically normalized to `127.0.0.1`. |
| Endpoint shows `[::]` | IPv6 any binding | Normalized to `[::1]`. |
| PR URL not mapping | Naming mismatch | Ensure `{Service}-PR{id}` app exists (capitalisation ignored). |
| Cert auth fails | Cert not in store or ACL | Verify certificate in `LocalMachine\My` & private key access. |

---

## Contributing

1. Run unit + integration tests before PR.
2. Add tests for new parsing / caching / error logic.
3. Keep documentation (README + comments) updated.
4. Prefer minimal allocations & thread‑safe code paths.
5. Follow existing logging patterns.

---

## License

Acetone is released under the [MIT License](LICENSE).
