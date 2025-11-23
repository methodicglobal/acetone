# Test Report – Windows Agent Plan (Local SF + HTTPS)

Date: 2025-11-23  
Environment: Windows 11, .NET 10.0.100, local SF dev cluster (localhost:19000), localhost cert thumbprint `697463038b881aaf1760f2e3397bae991bd2e534`.

## Status Tracking
- [x] Step 1: Environment Verification (.NET OK, SF cluster healthy)
- [x] Step 2: Build & Unit Tests (`dotnet test Acetone.V2.sln` – pass)
- [x] Step 3: Target Service Deployment (`fabric:/TestApp` -> `https://localhost:8987`)
- [x] Step 4: Proxy Execution & Routing (`http://testapp.localhost:8080/weatherforecast` -> 200 OK)
- [ ] Step 5: Resilience & Chaos (not executed in this run)
- [x] Step 6: Performance Benchmarking (cache/resolve benchmarks run)

## Key Commands Run
- Proxy: `dotnet run --project src/Acetone.V2.Proxy/Acetone.V2.Proxy.csproj --urls http://localhost:8080`
- Backend deploy: `msbuild MockApplication/MockApplication.sfproj /t:Package /p:Configuration=Debug` + SF PowerShell `Copy/Register/New-ServiceFabricApplication`
- Routing check: `curl http://testapp.localhost:8080/weatherforecast` (200)
- Benchmarks: `dotnet run -c Release --project tests/Acetone.V2.Performance/Acetone.V2.Performance.csproj --filter '*'`

## Observations
- Resolver normalizes machine-name endpoints to `localhost`, preventing TLS name mismatch when proxying to the HTTPS backend.
- Backend reachable directly: `https://localhost:8987/weatherforecast` returns 200.
- Proxy routing over HTTPS succeeds with localhost cert trust in place.

## Benchmark Highlights (Cache/Resolver)
- `ResolveUrl_CacheHit`: mean ~1.06 µs, alloc ~1.33 KB/op
- `ResolveUrl_CacheMiss`: mean ~2.53 µs, alloc ~2.96 KB/op
Artifacts: `tests/BenchmarkDotNet.Artifacts/results/`.

## Next Actions
- Execute resilience/chaos scenarios (stop/restart MockApi while proxy runs) and capture HTTP status behavior.
- Consider adding HTTPS hostname guidance to operational docs (cert trust, SANs).
