# Acetone V2 (YARP, .NET 10) – Service Fabric URL & Endpoint Resolver

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)
[![YARP](https://img.shields.io/badge/Reverse_Proxy-YARP-green.svg)](https://microsoft.github.io/reverse-proxy/)
[![Service Fabric](https://img.shields.io/badge/Service%20Fabric-8.2.274-purple.svg)](https://learn.microsoft.com/azure/service-fabric/)

Acetone turns friendly hostnames (including pull‑request previews) into live Service Fabric endpoints and wires them into a reverse proxy. **V2** is the primary line: built on **.NET 10**, ships a **YARP** proxy, cross‑platform tray app, installers, and ready‑to-run binaries for Windows and Linux.

> Looking for the legacy IIS / .NET Framework 4.8.1 line? See **Legacy V1 (.NET Framework 4.8.1, IIS module)** below. Keep it only if you must run on IIS/.NET 4.8.1; otherwise move to V2.

---
## Quick Links
- **V2 stable (YARP, .NET 10)**: `v2.0.0` release – assets `acetone-v2-win-x64.zip`, `acetone-v2-linux-x64.tar.gz`
- **V2 preview**: `v2.0.0-preview` – same assets, for early testing
- **Legacy V1 (.NET 4.8.1/IIS)**: releases `v1.5`, `v1.6` – assets `acetone-net481.zip`, `Methodic.Acetone.1.x.0.nupkg`
- Docker (V2): `ghcr.io/methodicglobal/acetone/acetone-v2-proxy:v2.0.0`

---
## What’s in V2
- **Reverse proxy**: YARP-based, compiled for .NET 10, multi-platform.
- **Tray app**: Windows and Linux tray companions (status, quick actions, auto-start scripts).
- **Installers**: `install.ps1` (Windows), `install.sh` / `install-tray.sh` (Linux) to set up as a service/daemon and tray app.
- **Config-first**: Ships `appsettings.example.json`; supports PR URL normalization (`service-1234` → `Service-PR1234`), partition discovery, caching.
- **Artifacts include**: proxy binary, tray binary, installers, README, example config, `VERSION.txt`.

---
## Install & Run (V2)
### Windows (x64)
1) Download `acetone-v2-win-x64.zip` from the `v2.0.0` release.  
2) Extract: `Expand-Archive acetone-v2-win-x64.zip -DestinationPath acetone-v2`.  
3) Install as a service + tray:
```powershell
cd acetone-v2
.\install.ps1 -CreateService -StartService -InstallTrayApp
# Optional: configure autostart tray only
.\install-tray.ps1
```
4) Configure: edit `appsettings.json` (copy from `appsettings.example.json`).  
5) Run manually (no service): `.\Acetone.V2.Proxy.exe`.

### Linux (x64)
1) Download `acetone-v2-linux-x64.tar.gz` from the `v2.0.0` release.  
2) Extract: `tar -xzf acetone-v2-linux-x64.tar.gz -C acetone-v2`.  
3) Install service + tray:
```bash
cd acetone-v2
sudo ./install.sh --create-service --start-service --install-tray-app
sudo ./install-tray.sh   # tray autostart helper (systemd user)
```
4) Desktop entry for tray is included: `acetone-tray.desktop`.  
5) Run manually: `./Acetone.V2.Proxy`.

### Docker (V2)
```bash
docker pull ghcr.io/methodicglobal/acetone/acetone-v2-proxy:v2.0.0
docker run -p 8080:8080 -p 9090:9090 ghcr.io/methodicglobal/acetone/acetone-v2-proxy:v2.0.0
```

### Configuration Highlights (V2)
- `appsettings.json`:
  - `ClusterConnectionStrings`: comma-separated SF gateways.
  - `ApplicationNameLocation`: `Subdomain` (default) or `FirstUrlFragment` variants.
  - PR normalization: `service-1234` → `Service-PR1234`.
  - Caching: application/service/partition caches with warmup.
  - Logging: structured, optional verbose traces.
- `VERSION.txt`: built-in provenance (commit, platform, timestamp).

---
## When to Stay on Legacy V1 (.NET 4.8.1, IIS)
| Use V1 if… | Why |
|------------|-----|
| You must run inside **IIS** as a rewrite provider on **.NET Framework 4.8.1** | V1 ships as a DLL + PDB and a NuGet package (`Methodic.Acetone.1.x.0.nupkg`). |
| You have existing IIS rewrite rules that directly load `ServiceFabricLocator` | V1 drop-in keeps those working. |
| You cannot host YARP / .NET 10 on the target estate yet | Use V1 temporarily and plan migration. |

### Getting V1
- Releases: `v1.5`, `v1.6`
  - `acetone-net481.zip` (binaries)
  - `Methodic.Acetone.1.5.0.nupkg`, `Methodic.Acetone.1.6.0.nupkg`
- Minimal IIS provider snippet:
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
> Recommendation: migrate to V2 (YARP, .NET 10) for cross-platform support, modern runtime, and built-in installers/tray tooling.

---
## Release Matrix
| Track | Runtime / Proxy | Artifacts | Latest Tags | Notes |
|-------|-----------------|-----------|-------------|-------|
| **V2 (Recommended)** | .NET 10, YARP | `acetone-v2-win-x64.zip`, `acetone-v2-linux-x64.tar.gz`, Docker image | `v2.0.0` (stable), `v2.0.0-preview` | Cross-platform, installers, tray apps, PR URL normalization, services/daemon scripts. |
| **V1 (Legacy)** | .NET Framework 4.8.1, IIS rewrite provider | `acetone-net481.zip`, `Methodic.Acetone.1.x.0.nupkg` | `v1.6`, `v1.5` | Keep only for IIS/.NET 4.8.1 estates. |

---
## Development & Testing
### V2 (YARP / .NET 10)
- Build: `dotnet build acetone-v2/Acetone.V2.sln -c Release`
- Test: `dotnet test acetone-v2/Acetone.V2.sln -c Release --logger "trx;LogFileName=test-results.trx"`
- Docker build: `docker build -f acetone-v2/src/Acetone.V2.Proxy/Dockerfile -t acetone-v2-proxy .`

### V1 (IIS / .NET 4.8.1)
- Build: `dotnet build Methodic.Acetone.CoreOnly.sln -c Release`
- Test (mock mode):  
```powershell
$env:ACETONE_SKIP_DEPLOY='1'; $env:ACETONE_SKIP_APP_TYPE_UNPROVISION='1'
dotnet test Methodic.Acetone.Tests/Methodic.Acetone.Tests.csproj -c Release -p:Platform=x64
```
- Pack NuGet: `nuget pack Methodic.Acetone.nuspec -Version 1.6.0 -Properties Configuration=Release`

---
## Configuration Cheats (applies to both)
| Setting | Purpose |
|---------|---------|
| `ClusterConnectionStrings` | Comma-separated SF gateways (required). |
| `ApplicationNameLocation` | `Subdomain` (default), `SubdomainPreHyphens`, `SubdomainPostHyphens`, `FirstUrlFragment`. |
| `EnableLogging` | Enable structured logging (disable for steady-state perf). |
| `CredentialsType` | `Local`, `CertificateThumbprint`, or `CertificateCommonName`. |
| `ServerCertificateCommonNames` / `ServerCertificateThumbprints` | Pin remote SF gateways. |
| Partition cache | Enabled by default; disable with `ACETONE_DISABLE_PARTITION_CACHE=1` (diagnostics only). |

PR normalization rule: `{service}-{digits}` → `{Service}-PR{digits}` (case-normalized, digits at end only).

---
## Tray Apps
- **Windows tray**: included in `acetone-v2-win-x64.zip` under `tray`; install via `install-tray.ps1` or `install.ps1 -InstallTrayApp`. Supports autostart and status UI.
- **Linux tray**: included in `acetone-v2-linux-x64.tar.gz` under `tray`; install via `install-tray.sh`; desktop entry provided (`acetone-tray.desktop`).

---
## Contributing
1) Fork & branch (`feature/*` or `fix/*`).  
2) Prefer V2 paths (`acetone-v2`); keep V1 changes minimal and backward-compatible.  
3) Add/adjust tests; keep PR normalization and cache behaviour covered.  
4) Run the relevant solution tests before PR (`V2` or `V1` as above).  
5) Update docs when behaviour changes.  

Coding guidelines: thread-safe caches, low allocation in hot paths, clear error semantics, and concise logging (avoid noisy debug in production).

---
## License
MIT – see [LICENSE](LICENSE).
