# Acetone V2 (YARP + Kestrel)

Quick start for local development on Windows and Linux with a Service Fabric backend.

## Prerequisites
- .NET 10 SDK
- YARP-compatible runtime (Kestrel)
- Service Fabric SDK/CLI and a reachable cluster (local or remote)
- Cert for HTTPS backend (local dev: localhost cert thumbprint `697463038b881aaf1760f2e3397bae991bd2e534`)

## Windows installation
1) Install Service Fabric SDK (includes PowerShell module) and start a local cluster (1- or 5-node).  
2) Ensure localhost cert is trusted: `Get-ChildItem Cert:\LocalMachine\My\697463038b881aaf1760f2e3397bae991bd2e534` (import if missing).  
3) Package sample app:
   ```powershell
   msbuild MockApplication/MockApplication.sfproj /t:Package /p:Configuration=Debug
   ```
4) Deploy sample app to local cluster:
   ```powershell
   Import-Module 'C:\Program Files\Microsoft Service Fabric\bin\ServiceFabric\Microsoft.ServiceFabric.Powershell.dll'
   Connect-ServiceFabricCluster -ConnectionEndpoint localhost:19000
   Copy-ServiceFabricApplicationPackage -ApplicationPackagePath "$PWD\MockApplication\pkg\Debug" `
     -ImageStoreConnectionString 'file:C:\SfDevCluster\Data\ImageStoreShare' `
     -ApplicationPackagePathInImageStore 'TestApp' -Force
   Register-ServiceFabricApplicationType -ApplicationPathInImageStore 'TestApp'
   New-ServiceFabricApplication -ApplicationName fabric:/TestApp -ApplicationTypeName MockApplicationType `
     -ApplicationTypeVersion 1.0.0 `
     -ApplicationParameter @{ MockApi_InstanceCount='1'; MockApi_ASPNETCORE_ENVIRONMENT='Production' }
   ```
   MockApi listens on `https://localhost:8987` using the localhost cert.
5) Run proxy:
   ```powershell
   dotnet run --project src/Acetone.V2.Proxy/Acetone.V2.Proxy.csproj --urls http://localhost:8080
   ```
6) Verify:
   ```powershell
   curl http://testapp.localhost:8080/weatherforecast   # Expect 200
   ```

## Linux installation (clusters reachable over HTTPS)
1) Install .NET 10 SDK and Service Fabric CLI (sfctl) or connect to an existing SF cluster (e.g., `sfctl cluster select --endpoint https://cluster:19080` with appropriate cert/key).  
2) Ensure a trusted cert exists for the backend services. If using the sample, update `MockApi/PackageRoot/ServiceManifest.xml` to point to your cert thumbprint or switch to HTTP for local-only use.  
3) Publish the sample app on a Windows machine (SF packaging tooling is Windows-focused) or use an existing SF app. Copy the package to the cluster’s image store (`sfctl application upload/register/create`).  
4) Update `src/Acetone.V2.Proxy/appsettings.json`:
   - `Acetone:ClusterConnectionStrings`: your cluster endpoint(s)
   - `CredentialsType`: set to the auth mode required by the cluster
   - `ApplicationNameLocation`: as needed (Subdomain, etc.)
5) Run proxy:
   ```bash
   dotnet run --project src/Acetone.V2.Proxy/Acetone.V2.Proxy.csproj --urls http://0.0.0.0:8080
   ```
6) Verify routing to your SF app (adjust host/path to match your ApplicationNameLocation).

## Tests
- Unit/integration: `dotnet test Acetone.V2.sln`
- Performance benchmarks: `dotnet run -c Release --project tests/Acetone.V2.Performance/Acetone.V2.Performance.csproj --filter '*'`
