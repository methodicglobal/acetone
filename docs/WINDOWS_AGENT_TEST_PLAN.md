# Windows Agent Testing Plan for Acetone V2

## Objective
Rigorous validation of Acetone V2 on a Windows environment with a local Service Fabric cluster. This plan is designed for an autonomous agent to execute step-by-step.

## Environment Prerequisites
- **OS**: Windows 10/11 or Server 2019+
- **Runtime**: .NET 10 SDK
- **Service Fabric**: Local Cluster running (1 node or 5 node)
- **Tools**: PowerShell, git

## Status Tracking
- [ ] **Step 1**: Environment Verification
- [ ] **Step 2**: Build & Unit Tests
- [ ] **Step 3**: Target Service Deployment
- [ ] **Step 4**: Proxy Execution & Routing Verification
- [ ] **Step 5**: Resilience & Chaos Testing
- [ ] **Step 6**: Performance Benchmarking

---

## Execution Instructions

### Step 1: Environment Verification
**Goal**: Confirm the environment is healthy and ready for testing.

**Agent Prompt**:
> "Run a diagnostic check on the current environment. Verify that:
> 1. .NET 10 SDK is installed (`dotnet --version`).
> 2. Service Fabric Local Cluster is running and healthy. Use PowerShell `Connect-ServiceFabricCluster` and `Get-ServiceFabricClusterHealth`.
> 3. The `Acetone.V2` repository is cloned and accessible."

---

### Step 2: Build & Unit Tests
**Goal**: Ensure the codebase compiles and passes all core logic tests before deployment.

**Agent Prompt**:
> "Navigate to the repository root. Restore all dependencies and run the full unit test suite.
> Command: `dotnet test Acetone.V2.sln`
> 
> **Success Criteria**:
> - Build succeeds with 0 errors.
> - All 87+ unit tests pass.
> - Report any failures immediately."

---

### Step 3: Target Service Deployment
**Goal**: Deploy a real Service Fabric service to route traffic to.

**Instructions**:
You need a target service to test the proxy. If one does not exist, create a simple one.

**Agent Prompt**:
> "Check if there is a running Service Fabric application named `fabric:/TestApp`.
> If not, create a new stateless ASP.NET Core service named `TestApp` with a service `TestService` listening on port 8081.
> 
> **Action Plan**:
> 1. Create a new SF project `TestApp` if needed (or use a pre-existing test fixture).
> 2. Deploy it to the local cluster.
> 3. Verify it is healthy using `Get-ServiceFabricApplicationHealth fabric:/TestApp`.
> 4. Confirm the endpoint is accessible locally (e.g., `http://localhost:8081/`)."

---

### Step 4: Proxy Execution & Routing Verification
**Goal**: Run Acetone V2 Proxy and verify it correctly routes requests to the target service.

**Instructions**:
1. Configure `appsettings.json` to use `CredentialsType: Windows` (or `Local` if insecure cluster).
2. Run the proxy on port 8080.
3. Send a request to `http://testapp.localhost:8080/` (assuming subdomain routing) and verify it reaches `fabric:/TestApp/TestService`.

**Agent Prompt**:
> "Configure and run the Acetone V2 Proxy:
> 1. Modify `src/Acetone.V2.Proxy/appsettings.json`:
>    - Set `Acetone:CredentialsType` to `Windows` (or appropriate for local cluster).
>    - Set `Acetone:ApplicationNameLocation` to `Subdomain`.
> 2. Start the proxy: `dotnet run --project src/Acetone.V2.Proxy/Acetone.V2.Proxy.csproj --urls http://localhost:8080`
> 
> **Verification**:
> - In a separate terminal, run `curl -v http://testapp.localhost:8080/`.
> - **Expected Result**: 200 OK from the backend service.
> - If it fails, check proxy logs for resolution errors."

---

### Step 5: Resilience & Chaos Testing
**Goal**: Verify that the proxy handles backend failures gracefully (retries, circuit breaking).

**Agent Prompt**:
> "Perform chaos testing while the proxy is running:
> 1. **Scenario A (Transient)**: Restart the `TestService` instance. Send requests during the restart. Verify that the proxy retries and eventually succeeds (or returns 503 if down too long).
> 2. **Scenario B (Downtime)**: Stop the `TestService`. Send requests. Verify the proxy returns 503 Service Unavailable immediately (or after retries).
> 3. **Scenario C (Recovery)**: Start the `TestService` again. Verify traffic resumes.
> 
> Record the HTTP status codes observed during these transitions."

---

### Step 6: Performance Benchmarking
**Goal**: Run the included benchmarks on the Windows machine to validate performance in the actual environment.

**Agent Prompt**:
> "Run the performance benchmarks to establish a baseline on this hardware.
> Command: `dotnet run -c Release --project tests/Acetone.V2.Performance/Acetone.V2.Performance.csproj --filter '*'`
> 
> **Deliverable**:
> - Capture the output table showing Mean execution time and Allocations.
> - Confirm `ResolveUrl_CacheHit` is under 1 microsecond."

---

## Final Report
After completing all steps, generate a `TEST_REPORT.md` summarizing:
- Environment details.
- Test results (Pass/Fail).
- Benchmark metrics.
- Any issues encountered.
