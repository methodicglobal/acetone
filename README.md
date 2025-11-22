# Methodic Acetone – Service Fabric URL & Endpoint Resolver for IIS / Reverse Proxy

Acetone is a focused **open‑source fintech infrastructure component** that converts friendly hostnames (including pull‑request preview URLs) into live Service Fabric service endpoints at runtime. It ships as an IIS rewrite provider / helper library targeting **.NET Framework 4.8.1** with supporting mock & integration assets (some on **.NET 9**) to validate real‑world deployment and performance.

## Deployment

### Docker
Build the Docker image:
```bash
docker build -f src/Acetone.V2.Proxy/Dockerfile -t acetone-proxy .
```
Run locally:
```bash
docker run -p 8080:8080 acetone-proxy
```

### Kubernetes
Deploy using Helm:
```bash
helm install acetone-proxy ./charts/acetone-proxy
```

## Monitoring
- Metrics are available at `/metrics` in Prometheus format.
- Health checks are available at `/health/live` and `/health/ready`.
- Distributed tracing is enabled via OpenTelemetry.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET Framework 4.8.1](https://img.shields.io/badge/.NET-4.8.1-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework)
[![Service Fabric](https://img.shields.io/badge/Service%20Fabric-8.2.274-purple.svg)](https://learn.microsoft.com/azure/service-fabric/)

---
## Table of Contents
1. Why Acetone
2. Repository Layout & Solutions
3. Quick Start (Binary Usage)
4. Configuration Cheat Sheet
5. Pull‑Request Routing Logic
6. Endpoint Parsing & Normalisation
7. Caching Model
8. Testing Strategy & Environment Variables
9. Development & Build Workflow
10. Architecture Overview
11. NuGet Packaging
12. Usage Examples
13. Reference (Parsing Modes / Errors / Performance)
14. Contributing
15. License

---
## 1. Why Acetone?
Fintech platforms increasingly operate multi‑tenant / ephemeral environments (preview apps per pull request, blue/green rotations, short‑lived test apps). Hard‑coding cluster service URIs into proxies introduces deploy friction & security risk.

Acetone provides:
- **Dynamic discovery** – Service Fabric application & service resolution (stateless API & FUNCTION heuristics) with partition endpoint extraction.
- **PR preview support** – `service-1234.domain` → `Service-PR1234` transformation (capitalises first letter only, normalises remainder).
- **Low‑latency caching** – Application, service & partition caches with TTL + notification invalidation; warmup on start but fully resilient when cluster offline.
- **Robust & explicit error semantics** – Distinguishes invalid URL, missing application, empty input, etc.
- **Secure cluster auth** – Local dev (no cert), certificate thumbprint, or subject / issuer DN with remote CN validation.
- **Portability & testability** – Pure library; unit + mock integration tests run without Service Fabric tooling; optional real cluster integration layer isolated.
- **Open source quality bar** – Clear code paths, minimal allocations in hot resolution loop, deterministic mocks, documented behaviour.

---
## 2. Repository Layout & Solutions
| Path / File | Purpose |
|-------------|---------|
| `Methodic.Acetone/` | Core library (.NET Framework 4.8.1) published to NuGet / release artifacts. |
| `Methodic.Acetone.Tests/` | Unit & mock integration tests (no Service Fabric runtime required). |
| `Methodic.Acetone.IntegrationTests/` | Optional real cluster integration tests (require local Service Fabric dev cluster & tooling). |
| `MockApi/`, `MockService/` | Sample .NET 9 minimal web services used when packaging Service Fabric test applications. |
| `MockApplication.sfproj`, `MockSystem.sfproj` | Service Fabric application packages referencing the mock services (excluded from CI build). |
| `Methodic.Acetone.nuspec` | NuGet packaging specification (non‑SDK project scenario). |
| `.github/workflows/dotnet.yml` | CI: builds core solution, runs unit tests, produces binary ZIP & `.nupkg` release. |
| `README.md` | This documentation. |

### Solution & Filter Strategy
To balance fast CI, simple contributor onboarding, and optional advanced testing:

| File | Contains | Intended Use |
|------|----------|--------------|
| `Methodic.Acetone.CoreOnly.sln` | Library + unit/mock tests only | CI pipeline & everyday contributors. Fast restore/build. |
| `Methodic.Acetone.Integration.sln` | Library + unit tests + integration test project | Local dev validating against a real Service Fabric cluster. |
| `Methodic.Acetone.slnf` | Solution filter referencing a lean subset of the *full* solution (when opened in Visual Studio) | Quick IDE load without removing projects on disk. |
| Original full solution (`Methodic.Acetone.sln`) | (If opened) may include additional SF app projects (mock deployments) | Advanced local scenarios; intentionally **not** used in CI. |

> CI always builds `Methodic.Acetone.CoreOnly.sln` to avoid Service Fabric build tool prerequisites and to keep pipelines deterministic.

---
## 3. Quick Start (Binary Usage Only)
### 3.1 Build from Source (Core Only)
```powershell
dotnet build Methodic.Acetone.CoreOnly.sln -c Release
```
Result: `Methodic.Acetone\bin\Release\Methodic.Acetone.dll`.

### 3.2 Install via GitHub Release NuGet Package
Download the latest `.nupkg` from Releases (produced automatically). (Public NuGet.org publication planned – track repository issues.)

### 3.3 Minimal IIS Rewrite Provider Configuration
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
## 4. Configuration Cheat Sheet
| Setting | Purpose | Notes |
|---------|---------|-------|
| `ClusterConnectionStrings` | Comma separated SF gateways | REQUIRED. Example: `node1:19000,node2:19000`. |
| `ApplicationNameLocation` | Derive application name position | `Subdomain` (default), `SubdomainPreHyphens`, `SubdomainPostHyphens`, `FirstUrlFragment`. |
| `EnableLogging` | Diagnostic logging | Avoid in high‑throughput steady state. |
| `CredentialsType` | Cluster auth mode | `Local`, `CertificateThumbprint`, `CertificateCommonName`. |
| `ClientCertificateThumbprint` | Thumbprint for client auth | Required for Thumbprint mode. |
| `ServerCertificateThumbprints` | Remote gateway certs | Optional extra pinning. |
| `ClientCertificateSubjectDistinguishedName` | Subject DN | Required for CommonName mode. |
| `ClientCertificateIssuerDistinguishedName` | Issuer DN | Required for CommonName mode. |
| `ServerCertificateCommonNames` | Accepted remote CNs | Optional list. |
| `PartitionCacheLimit` | Fabric client partition cache size | Default 5 (in FabricClient settings). |
| `VersionParameter`, `ClearCacheParameter` | Reserved | Future UX additions. |

---
## 5. Pull‑Request Routing Logic
Pattern active in `Subdomain` + `FirstUrlFragment` modes:
```
{base}-{digits}  =>  {Base}-PR{digits}
```
Rules:
- Only terminal numeric segment triggers transformation.
- First character upper‑cased; remainder lower‑cased (`API-1234` → `Api-PR1234`).
- Hyphenated names without trailing digits are left untouched (`my-service` stays `my-service`).

---
## 6. Endpoint Parsing & Normalisation
- Accepts raw `http(s)://host[:port]` or standard Service Fabric endpoint JSON (`{"Endpoints":{"":"https://host:port"}}`).
- Selects first HTTP/S endpoint (ignores remoting endpoints when present).
- IPv6 supported (bracketed form); normalises any‑address to loopback:
  - `0.0.0.0` → `127.0.0.1`
  - `[::]` → `[::1]`

---
## 7. Caching Model
| Cache | Key | Invalidation | Implementation | Notes |
|-------|-----|-------------|----------------|-------|
| Applications | `APPNAME[-version]` | Manual refresh flag | Lazy `ConcurrentDictionary` | Populated on demand & optional warmup. |
| Services | Application URI | SF notification filter | Lazy `ConcurrentDictionary` | Heuristic: single stateless *API* / *Service* (or *FUNCTION*). |
| Partitions | Service URI | TTL (30s) or disabled | `ConcurrentDictionary` | Disable via `ACETONE_DISABLE_PARTITION_CACHE=1`. |

---
## 8. Testing Strategy & Environment Variables
| Layer | Project | CI | SF Required | Description |
|-------|---------|----|-------------|-------------|
| Unit + Mock Integration | `Methodic.Acetone.Tests` | Yes | No | Fast deterministic tests using in‑memory resolvers. |
| Real Cluster Integration | `Methodic.Acetone.IntegrationTests` | No | Yes (optional) | Deploys mock apps & synthetic apps to local cluster. |

Key environment variables:
| Variable | Effect |
|----------|-------|
| `ACETONE_SKIP_DEPLOY=1` | Skip deploying test apps (force mock mode). |
| `ACETONE_SKIP_APP_TYPE_UNPROVISION=1` | Preserve app types after tests. |
| `ACETONE_DISABLE_PARTITION_CACHE=1` | Turn off partition cache for diagnostics. |
| `ACETONE_FORCE_DISPOSE_CLEANUP=1` | Force cleanup during disposable teardown if explicit cleanup not invoked. |

Run (core only):
```powershell
dotnet test Methodic.Acetone.Tests\Methodic.Acetone.Tests.csproj -c Release -p:Platform=x64
```

Run integration suite (local cluster):
```powershell
dotnet test Methodic.Acetone.IntegrationTests\Methodic.Acetone.IntegrationTests.csproj -c Release -p:Platform=x64
```

---
## 9. Development & Build Workflow
| Scenario | Command |
|----------|---------|
| Restore & Build (core) | `dotnet build Methodic.Acetone.CoreOnly.sln -c Release` |
| Run Unit Tests | `dotnet test Methodic.Acetone.Tests\Methodic.Acetone.Tests.csproj -c Release -p:Platform=x64` |
| Open fast in VS | Open `Methodic.Acetone.slnf` (filter) |
| Full local integration | Open `Methodic.Acetone.Integration.sln` |
| Run Integration Tests | `dotnet test Methodic.Acetone.IntegrationTests\Methodic.Acetone.IntegrationTests.csproj -c Release -p:Platform=x64` |
| Produce NuGet (manual) | `nuget pack Methodic.Acetone.nuspec -Version 0.1.0 -Properties Configuration=Release` |

---
## 10. Architecture Overview
| Component | Responsibility |
|-----------|----------------|
| `ServiceFabricUrlParser` | Application name extraction & endpoint JSON parsing. |
| `ServiceFabricUrlResolver` | Cluster query orchestration + caches + partition resolution with retry/backoff. |
| `ServiceFabricLocator` | IIS rewrite provider entry; settings validation & resolver lifecycle. |
| `MockServiceFabricUrlResolver` / `TestableServiceFabricUrlResolver` | Deterministic test doubles for high‑volume tests. |
| `ServiceFabricTestClusterManager` | (Integration only) Deploys & cleans synthetic + solution SF apps. |

---
## 11. NuGet Packaging
CI produces a `.nupkg` using the dedicated **non‑SDK** `Methodic.Acetone.nuspec` (keeps legacy project structure intact). Contents:
- `lib/net481/Methodic.Acetone.dll` (+ PDB)
- Metadata (MIT license, repository links)

Manual pack example:
```powershell
nuget pack Methodic.Acetone.nuspec -Version 0.2.0 -Properties Configuration=Release
```
_Upcoming:_ Source link & symbol package, SDK‑style project migration (without breaking existing consumers), publication to NuGet.org.

---
## 12. Usage Example
```csharp
using Methodic.Acetone;

var logger = new TraceLogger { Enabled = true };
using var resolver = new ServiceFabricUrlResolver(logger, "localhost:19000");
if (ServiceFabricUrlParser.TryGetApplicationNameFromUrl("https://guard-12906.example.com", ApplicationNameLocation.Subdomain, out var appName))
{
    var endpoint = await resolver.ResolveServiceUri(appName, Guid.NewGuid());
    Console.WriteLine($"Resolved endpoint: {endpoint}");
}
```

---
## 13. Reference
### 13.1 URL Parsing Modes
| Mode | Example | Extracted |
|------|---------|-----------|
| Subdomain | `https://myservice.company.com` | `myservice` |
| SubdomainPreHyphens | `https://myservice-uat-01.company.com` | `myservice` |
| SubdomainPostHyphens | `https://uat-01-myservice.company.com` | `myservice` |
| FirstUrlFragment | `https://api.company.com/myservice/v1` | `myservice` |

### 13.2 PR Pattern Examples
| Input | Output | Notes |
|-------|--------|-------|
| `guard-12906` | `Guard-PR12906` | Digits suffix triggers PR transform |
| `API-1234` | `Api-PR1234` | Case normalised |
| `service-999` | `Service-PR999` | Any digit count |
| `my-service` | `my-service` | Not a PR (no trailing digits) |

### 13.3 Error Semantics
| Scenario | Exception |
|----------|-----------|
| Null URL | `ArgumentNullException` |
| Empty / whitespace URL | `ArgumentException` |
| Single label host (invalid) | `ArgumentException` |
| Application not found | `KeyNotFoundException` |

### 13.4 Performance Tips
1. Reuse a single resolver instance per process / app domain.
2. Order `ClusterConnectionStrings` by expected latency.
3. Leave logging off (unless diagnosing) to reduce contention.
4. Avoid disabling the partition cache in production.
5. Resolve critical applications once at startup (warm caches).

### 13.5 Common Issues
| Symptom | Cause | Action |
|---------|-------|--------|
| `KeyNotFoundException` | App name mismatch / not deployed | Verify SF Explorer naming & PR transform. |
| Normalised `127.0.0.1` or `[::1]` | Original endpoint was any‑address | Expected behaviour for local proxying. |
| Certificate failures | Missing cert / ACL | Install in `LocalMachine\My` and grant private key access. |
| Multiple stateless services matched | Naming heuristic ambiguity | Consolidate to a single *API* or *Service* (or specify version). |

---
## 14. Contributing
We welcome high‑quality fintech‑grade contributions:
1. Fork & branch (`feature/…` or `fix/…`).
2. Run unit tests (`Methodic.Acetone.CoreOnly.sln`).
3. (Optional) Validate integration solution if SF changes introduced.
4. Add / update tests for new behaviours.
5. Update README & XML docs where behaviour changes.
6. Submit PR with clear description & rationale.

Coding guidelines:
- Thread safety in shared caches.
- Avoid blocking waits inside hot paths (async where practical).
- Minimise allocations in resolution loops.
- Clear, actionable log messages (avoid noisy debug spam in production).

---
## 15. License
Released under the [MIT License](LICENSE). © Methodic Global.

---
**Status:** Active development – roadmap includes SDK project migration, advanced version routing, multi-cluster failover & adaptive caching telemetry.
