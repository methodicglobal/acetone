# Acetone V2 - YARP & Kestrel Implementation Plan

## Executive Summary

This document outlines the comprehensive plan to create **Acetone V2**, a cross-platform Service Fabric reverse proxy built on .NET 10, Kestrel, and YARP (Yet Another Reverse Proxy). This new version will replicate all features of the current IIS-based Acetone while enabling deployment on Linux and containerized environments.

---

## Context: What is Acetone?

**Acetone** is an open-source **Service Fabric URL resolver and reverse proxy** designed specifically for fintech-grade infrastructure. It solves the critical problem of dynamic service discovery in multi-tenant, ephemeral Service Fabric environments.

### Core Problem Solved

- Eliminates hard-coded cluster URIs in reverse proxies
- Enables dynamic service discovery for multi-tenant/ephemeral environments
- Supports PR preview applications with automatic URL-to-application name transformation
- Provides low-latency routing with resilient caching strategies

### Current Architecture (Acetone V1)

**Technology Stack:**
- .NET Framework 4.8.1
- IIS URL Rewrite Module
- Windows-only deployment
- FabricClient for Service Fabric integration

**Entry Point:**
- Custom IIS Rewrite Provider (`IRewriteProvider`)
- Configured via `web.config` in IIS applications
- Runs in-process within IIS worker process (w3wp.exe)

---

## What Acetone V1 Achieves

### 1. Dynamic Service Discovery

**Three-Level Discovery Process:**

1. **Application Discovery**
   - Queries Service Fabric cluster for all applications
   - Filters by application type name or application name
   - Handles version filtering
   - Caches results by `{appName}-{version}` key

2. **Service Discovery**
   - Queries services within discovered applications
   - Filters for stateless services containing "API"/"SERVICE" or "FUNCTION"
   - Expects exactly one matching service per application
   - Registers for Service Fabric topology change notifications

3. **Partition Resolution**
   - Resolves service partition endpoints with retry/backoff
   - Extracts endpoints from Service Fabric JSON format
   - Normalizes local addresses (`0.0.0.0` → `127.0.0.1`)
   - Caches with 30-second TTL

### 2. Pull Request URL Routing

**Automatic Pattern Detection:**
- `service-1234.company.com` → `Service-PR1234` (application name)
- `api.company.com/service-1234` → `Service-PR1234` (first URL fragment mode)
- First character capitalized, remainder lowercased
- Only applies to Subdomain and FirstUrlFragment modes
- Handles trailing numeric segments only

### 3. Application Name Location Modes

| Mode | Example Input | Extracted App Name |
|------|---------------|-------------------|
| **Subdomain** | `myservice.company.com` | `myservice` |
| **SubdomainPreHyphens** | `myservice-uat-01.company.com` | `myservice` |
| **SubdomainPostHyphens** | `uat-01-myservice.company.com` | `myservice` |
| **FirstUrlFragment** | `api.company.com/myservice/endpoint` | `myservice` |

### 4. URL Handling Features

**Endpoint Parsing:**
- Handles Service Fabric JSON: `{"Endpoints":{"":"https://host:port"}}`
- Supports plain URLs, IPv4, and IPv6 (bracketed) addresses
- Filters out remoting endpoints, selects HTTP/HTTPS only

**URL Sanitization:**
- Handles malformed URLs with extra colon-separated segments
- Pattern: `https://host:443:2403:5818:d5fa:10::177/` → `https://host:443/`
- Graceful fallback for production edge cases

**Normalization:**
- Treats underscores and hyphens as equivalent in app names
- `Guard-PR12906` URL → matches `Guard_PR12906` cluster deployment

### 5. Resilience & Caching

**Three-Tier Cache Strategy:**

| Cache Level | Key | TTL | Invalidation |
|------------|-----|-----|--------------|
| **Applications** | `{AppName}-{version}` | None (lazy) | Manual refresh flag |
| **Services** | Application URI | None | Service notification events |
| **Partitions** | Service URI | 30 seconds | Time-based |

**Retry Logic:**
- Partition resolution: 10 attempts with exponential backoff
- Initial delay: 100ms, capped at 2 seconds
- Per-attempt timeout: 5 seconds
- Circuit breaker via `FabricTransientException` handling

**Cache Warmup:**
- Runs on initialization
- Pre-resolves all registered application types
- Non-blocking (continues on individual failures)

### 6. Security & Authentication

**Service Fabric Cluster Authentication (3 modes):**

1. **Local** (development only)
2. **CertificateThumbprint** - Thumbprint-based validation
3. **CertificateCommonName** - CN-based validation with issuer chains

**Certificate Discovery:**
- Searches `LocalMachine\My` store
- Supports X.509 certificate chains
- Validates remote server certificates

### 7. Logging & Observability

**Two Logging Modes:**
- **EventLogger** - Windows Event Log (production)
- **TraceLogger** - Trace output (development/testing)

**Logged Events:**
- URL resolution start/success/failure
- Cache operations (warmup, invalidation)
- Service Fabric topology changes
- Configuration validation errors
- Certificate discovery warnings

**Exception Formatting:**
- Comprehensive exception details with inner exceptions
- Recursive unwrapping for diagnostics
- Includes HResult, Source, StackTrace, TargetSite
- Invocation IDs for correlation

---

## What Acetone V2 Will Achieve

### Primary Goals

1. **Cross-Platform Deployment**
   - Run on Linux, Windows, macOS
   - Container-first architecture (Docker/Kubernetes)
   - No IIS dependency

2. **Modern Technology Stack**
   - .NET 10 (latest LTS at time of development)
   - Kestrel web server
   - YARP (Yet Another Reverse Proxy) for reverse proxy functionality
   - Minimal API or ASP.NET Core for HTTP pipeline

3. **Feature Parity with V1**
   - All URL resolution modes
   - Pull request routing
   - Three-tier caching
   - All authentication modes
   - Resilience patterns
   - Logging and observability

4. **Enhanced Capabilities**
   - Health check endpoints (`/health`, `/ready`)
   - Prometheus metrics endpoint
   - Structured logging (Serilog or built-in)
   - OpenTelemetry support
   - Configuration via environment variables, appsettings.json, or command-line
   - Hot reload of YARP routes
   - gRPC support (future consideration)

### Architecture Differences

| Aspect | Acetone V1 (IIS) | Acetone V2 (YARP) |
|--------|------------------|-------------------|
| **Web Server** | IIS | Kestrel |
| **Proxy** | IIS URL Rewrite | YARP |
| **Entry Point** | `IRewriteProvider` | YARP `IProxyConfigProvider` |
| **Framework** | .NET Framework 4.8.1 | .NET 10+ |
| **Deployment** | Windows only | Cross-platform |
| **Configuration** | `web.config` XML | `appsettings.json` + env vars |
| **Logging** | Event Log | Structured logging (ILogger) |
| **Process Model** | In-process (w3wp.exe) | Standalone service |
| **Metrics** | None | Prometheus + YARP built-in |
| **Health Checks** | None | ASP.NET Core health checks |
| **Container Support** | No | Yes (Docker, K8s) |

---

## Project Structure

### Folder Layout

```
acetone-v2/
├── Acetone.V2.sln.xml                    # .NET 10 SLNX solution file
├── src/
│   ├── Acetone.V2.Core/                  # Core library (portable)
│   │   ├── Acetone.V2.Core.csproj
│   │   ├── ServiceFabric/
│   │   │   ├── IServiceFabricResolver.cs
│   │   │   ├── ServiceFabricResolver.cs
│   │   │   ├── ServiceFabricUrlParser.cs
│   │   │   └── Models/
│   │   ├── Caching/
│   │   │   ├── ICache.cs
│   │   │   ├── ThreeTierCache.cs
│   │   │   └── CacheWarmer.cs
│   │   ├── Configuration/
│   │   │   ├── AcetoneOptions.cs
│   │   │   └── ApplicationNameLocation.cs
│   │   └── Resilience/
│   │       ├── RetryPolicy.cs
│   │       └── CircuitBreaker.cs
│   │
│   ├── Acetone.V2.Proxy/                 # YARP reverse proxy service
│   │   ├── Acetone.V2.Proxy.csproj
│   │   ├── Program.cs
│   │   ├── Yarp/
│   │   │   ├── ServiceFabricProxyConfigProvider.cs
│   │   │   ├── ServiceFabricProxyConfigFilter.cs
│   │   │   └── DynamicRouteBuilder.cs
│   │   ├── Middleware/
│   │   │   ├── ServiceFabricRoutingMiddleware.cs
│   │   │   └── ExceptionHandlingMiddleware.cs
│   │   ├── HealthChecks/
│   │   │   ├── ServiceFabricHealthCheck.cs
│   │   │   └── CacheHealthCheck.cs
│   │   ├── Metrics/
│   │   │   └── AcetoneMetrics.cs
│   │   └── appsettings.json
│   │
│   └── Acetone.V2.Sdk/                   # Optional: SDK for programmatic use
│       ├── Acetone.V2.Sdk.csproj
│       └── ServiceCollectionExtensions.cs
│
├── tests/
│   ├── Acetone.V2.Core.Tests/
│   │   ├── Acetone.V2.Core.Tests.csproj
│   │   └── ... (unit tests)
│   │
│   ├── Acetone.V2.Proxy.Tests/
│   │   ├── Acetone.V2.Proxy.Tests.csproj
│   │   └── ... (integration tests)
│   │
│   └── Acetone.V2.IntegrationTests/
│       ├── Acetone.V2.IntegrationTests.csproj
│       └── ... (real SF cluster tests)
│
├── samples/
│   ├── MockApi.V2/                       # .NET 10 mock API
│   ├── MockService.V2/                   # .NET 10 mock service
│   └── docker-compose.yml                # Local testing environment
│
├── deployment/
│   ├── docker/
│   │   ├── Dockerfile
│   │   └── .dockerignore
│   ├── kubernetes/
│   │   ├── deployment.yaml
│   │   ├── service.yaml
│   │   └── configmap.yaml
│   └── helm/
│       └── acetone-v2/
│
├── docs/
│   ├── MIGRATION_GUIDE.md
│   ├── CONFIGURATION.md
│   ├── DEPLOYMENT.md
│   └── ARCHITECTURE.md
│
└── README.md
```

---

## Implementation Steps

### Phase 1: Foundation & Core Library

#### Step 1.1: Project Setup
**Status:** `TODO`

**Tasks:**
- [ ] Create `acetone-v2/` directory in repository root
- [ ] Initialize .NET 10 solution with SLNX format: `dotnet new sln --name Acetone.V2 --use-program-main`
- [ ] Create `Acetone.V2.Core` class library project targeting .NET 10
- [ ] Create `Acetone.V2.Core.Tests` test project with xUnit
- [ ] Add NuGet dependencies:
  - `Microsoft.ServiceFabric` (latest .NET 10 compatible)
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Logging.Abstractions`
  - `System.Threading.RateLimiting` (for throttling)
- [ ] Configure project properties (nullable reference types, deterministic builds)
- [ ] Set up `.editorconfig` for consistent code style

**Acceptance Criteria:**
- Solution builds successfully
- Tests can be executed
- All projects target .NET 10

---

#### Step 1.2: Port Core URL Parsing Logic
**Status:** `TODO`

**Tasks:**
- [ ] Port `ServiceFabricUrlParser.cs` from V1 to V2
  - [ ] `ExtractApplicationName()` method
  - [ ] `ParseEndpointAddress()` method
  - [ ] `TryNormalizeUrlString()` method (malformed URL handling)
  - [ ] IPv4/IPv6 normalization logic
  - [ ] Query string preservation
- [ ] Port `ApplicationNameLocation` enum
- [ ] Port `EndpointAddressObject` model class
- [ ] Create comprehensive unit tests for all parsing scenarios
  - [ ] All ApplicationNameLocation modes
  - [ ] IPv6 bracket handling
  - [ ] Malformed URL patterns
  - [ ] Query string edge cases
  - [ ] Port normalization (underscore/hyphen)

**Acceptance Criteria:**
- All unit tests from V1 pass in V2
- 100% code coverage for parser logic
- No dependencies on IIS or Windows-specific APIs

---

#### Step 1.3: Implement Configuration System
**Status:** `TODO`

**Tasks:**
- [ ] Create `AcetoneOptions.cs` configuration class
  ```csharp
  public class AcetoneOptions
  {
      public string[] ClusterConnectionStrings { get; set; }
      public ApplicationNameLocation ApplicationNameLocation { get; set; }
      public CredentialsType CredentialsType { get; set; }
      public string? ClientCertificateThumbprint { get; set; }
      public string[]? ServerCertificateThumbprints { get; set; }
      public string? ClientCertificateSubjectDistinguishedName { get; set; }
      public string? ClientCertificateIssuerDistinguishedName { get; set; }
      public string[]? ServerCertificateCommonNames { get; set; }
      public int PartitionCacheLimit { get; set; } = 5;
      public int PartitionCacheTtlSeconds { get; set; } = 30;
      public int PartitionResolveMaxAttempts { get; set; } = 10;
      public int PartitionResolveInitialDelayMs { get; set; } = 100;
      public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(2);
      public bool EnableLogging { get; set; } = true;
      public bool DisablePartitionCache { get; set; } = false;
  }
  ```
- [ ] Create `AcetoneOptionsValidator` using `IValidateOptions<T>`
- [ ] Add support for environment variable overrides with `ACETONE_` prefix
- [ ] Create unit tests for configuration validation
  - [ ] Required fields validation
  - [ ] Certificate configuration validation
  - [ ] Timeout bounds checking

**Acceptance Criteria:**
- Configuration can be loaded from `appsettings.json`
- Environment variables override appsettings
- Invalid configurations throw descriptive exceptions
- All V1 configuration options are supported

---

#### Step 1.4: Implement Service Fabric Resolver
**Status:** `TODO`

**Tasks:**
- [ ] Create `IServiceFabricResolver` interface
- [ ] Port `ServiceFabricUrlResolver` class
  - [ ] FabricClient initialization with all 3 auth modes
  - [ ] Application discovery (`GetApplicationMetadata`)
  - [ ] Service discovery (`StatelessEndpointUri`, `FunctionEndpointUri`)
  - [ ] Partition resolution (`PartitionEndpoint`)
  - [ ] Service notification filter registration
  - [ ] Retry logic with exponential backoff
- [ ] Remove static state (make instance-based for DI)
- [ ] Replace Windows Event Log with `ILogger<T>`
- [ ] Create `ResolveUrlAsync(string url)` method as main entry point
- [ ] Add structured logging with semantic logging
  - [ ] Resolution start/success/failure events
  - [ ] Include correlation IDs
  - [ ] Log cache hit/miss statistics

**Acceptance Criteria:**
- Resolver can connect to Service Fabric cluster
- All authentication modes work
- Retry logic functions correctly
- Logging is structured and informative
- No static state (testable via DI)

---

#### Step 1.5: Implement Three-Tier Caching
**Status:** `TODO`

**Tasks:**
- [ ] Create `ICache<TKey, TValue>` interface
- [ ] Implement `MemoryCache<TKey, TValue>` wrapper around `System.Runtime.Caching`
- [ ] Create `ThreeTierCache` class managing:
  - [ ] Application cache (no expiration, manual invalidation)
  - [ ] Service cache (event-driven invalidation)
  - [ ] Partition cache (30-second TTL)
- [ ] Implement cache warmup logic (`WarmupCacheAsync`)
- [ ] Add cache statistics (hits, misses, evictions)
- [ ] Create `ICacheHealthCheck` for health check integration
- [ ] Add environment variable to disable partition cache (`ACETONE_DISABLE_PARTITION_CACHE`)
- [ ] Unit tests for cache behavior
  - [ ] TTL expiration
  - [ ] Event-driven invalidation
  - [ ] Concurrent access patterns
  - [ ] Warmup success/failure scenarios

**Acceptance Criteria:**
- Cache reduces Service Fabric API calls by >90%
- Cache invalidation responds to SF topology changes
- Cache statistics are exposed for metrics
- Thread-safe under concurrent load

---

#### Step 1.6: Implement Resilience Patterns
**Status:** `TODO`

**Tasks:**
- [ ] Add NuGet package: `Polly` (for retry/circuit breaker)
- [ ] Create `RetryPolicy` class wrapping Polly
  - [ ] Exponential backoff configuration
  - [ ] Max retry attempts
  - [ ] Per-attempt timeout
- [ ] Create `CircuitBreakerPolicy` for Service Fabric calls
  - [ ] Failure threshold configuration
  - [ ] Break duration
  - [ ] Half-open state handling
- [ ] Integrate policies into `ServiceFabricResolver`
- [ ] Add resilience metrics (retries, circuit breaker trips)
- [ ] Unit tests for resilience scenarios
  - [ ] Transient failure recovery
  - [ ] Circuit breaker activation
  - [ ] Timeout enforcement

**Acceptance Criteria:**
- Transient failures are automatically retried
- Circuit breaker prevents cascading failures
- Resilience events are logged and metered
- Polly policies are configurable via options

---

### Phase 2: YARP Integration & Proxy Service

#### Step 2.1: Create YARP Proxy Project
**Status:** `TODO`

**Tasks:**
- [ ] Create `Acetone.V2.Proxy` ASP.NET Core project
- [ ] Add NuGet package: `Yarp.ReverseProxy` (latest)
- [ ] Create `Program.cs` with minimal API setup
- [ ] Configure Kestrel with:
  - [ ] HTTP/HTTPS listeners
  - [ ] Connection limits
  - [ ] Request timeouts
  - [ ] HTTP/2 support
- [ ] Add default `appsettings.json` with Acetone configuration
- [ ] Add `appsettings.Development.json` for local testing
- [ ] Configure structured logging (Serilog or built-in)

**Acceptance Criteria:**
- Proxy service starts successfully
- Kestrel listens on configured ports
- Logs are structured and readable
- Configuration loads from appsettings

---

#### Step 2.2: Implement YARP Configuration Provider
**Status:** `TODO`

**Tasks:**
- [ ] Create `ServiceFabricProxyConfigProvider` implementing `IProxyConfigProvider`
- [ ] Implement `GetConfig()` method:
  - [ ] Create catch-all route matching all hostnames
  - [ ] Create cluster configuration (dynamic per request)
  - [ ] Configure load balancing (round-robin for multi-endpoint support)
  - [ ] Configure health checks (passive)
- [ ] Implement `IChangeToken` for dynamic configuration updates
- [ ] Create route metadata for correlation and metrics
- [ ] Unit tests for route generation
  - [ ] Route matching logic
  - [ ] Cluster configuration
  - [ ] Change token notification

**Acceptance Criteria:**
- YARP loads routes from provider
- Routes match all incoming requests
- Configuration updates trigger route reload
- Tests validate route generation logic

---

#### Step 2.3: Implement Service Fabric Routing Middleware
**Status:** `TODO`

**Tasks:**
- [ ] Create `ServiceFabricRoutingMiddleware` class
- [ ] Implement middleware logic:
  ```csharp
  public async Task InvokeAsync(HttpContext context)
  {
      // 1. Extract application name from request URL
      // 2. Call ServiceFabricResolver.ResolveUrlAsync()
      // 3. Set YARP destination via context.Features.Get<IReverseProxyFeature>()
      // 4. Handle resolution failures (404, 503 responses)
      // 5. Add correlation headers
      // 6. Call next middleware
  }
  ```
- [ ] Add middleware to pipeline before YARP
- [ ] Implement error handling:
  - [ ] `KeyNotFoundException` → 404 (application not found)
  - [ ] `FabricException` → 503 (Service Fabric unavailable)
  - [ ] `TimeoutException` → 504 (resolution timeout)
  - [ ] Generic exceptions → 500
- [ ] Add request/response logging with structured data
- [ ] Integration tests for middleware
  - [ ] Successful resolution and proxy
  - [ ] 404 scenarios
  - [ ] 503 scenarios
  - [ ] Timeout handling

**Acceptance Criteria:**
- Middleware successfully resolves SF endpoints
- Errors return appropriate HTTP status codes
- Correlation IDs flow through requests
- Structured logs include resolution details

---

#### Step 2.4: Implement YARP Transforms
**Status:** `TODO`

**Tasks:**
- [ ] Create `ServiceFabricProxyConfigFilter` implementing `IProxyConfigFilter`
- [ ] Configure request transforms:
  - [ ] Preserve original `Host` header (or override based on config)
  - [ ] Add `X-Forwarded-*` headers
  - [ ] Add correlation ID header (`X-Correlation-Id`)
  - [ ] Add Acetone version header (`X-Acetone-Version`)
  - [ ] Remove sensitive headers if needed
- [ ] Configure response transforms:
  - [ ] Add timing headers (`X-Acetone-Resolution-Time-Ms`)
  - [ ] Add cache hit/miss header (`X-Acetone-Cache-Hit`)
- [ ] Unit tests for transform logic

**Acceptance Criteria:**
- Headers are correctly transformed
- Original client information is preserved
- Debugging headers are added
- Tests validate all transforms

---

#### Step 2.5: Implement Health Checks
**Status:** `TODO`

**Tasks:**
- [ ] Add NuGet package: `Microsoft.Extensions.Diagnostics.HealthChecks`
- [ ] Create `ServiceFabricHealthCheck`:
  - [ ] Check FabricClient connectivity
  - [ ] Query cluster health status
  - [ ] Return Healthy/Degraded/Unhealthy
- [ ] Create `CacheHealthCheck`:
  - [ ] Check cache statistics
  - [ ] Validate warmup completion
  - [ ] Check cache hit ratio
- [ ] Add health check endpoints:
  - [ ] `/health` - Liveness probe (basic)
  - [ ] `/health/ready` - Readiness probe (includes SF connectivity)
  - [ ] `/health/live` - Liveness only
- [ ] Configure health check publishing (logs, metrics)
- [ ] Integration tests for health endpoints

**Acceptance Criteria:**
- Health endpoints return correct status
- Kubernetes probes can use `/health/ready` and `/health/live`
- Unhealthy SF cluster causes readiness failure
- Health checks are non-blocking and fast (<1s)

---

#### Step 2.6: Implement Metrics & Observability
**Status:** `TODO`

**Tasks:**
- [ ] Add NuGet packages:
  - [ ] `prometheus-net.AspNetCore`
  - [ ] `OpenTelemetry.Extensions.Hosting`
  - [ ] `OpenTelemetry.Instrumentation.AspNetCore`
  - [ ] `OpenTelemetry.Instrumentation.Http`
- [ ] Create `AcetoneMetrics` class with custom metrics:
  - [ ] `acetone_url_resolutions_total` (counter) - labels: status, cache_hit
  - [ ] `acetone_url_resolution_duration_seconds` (histogram)
  - [ ] `acetone_cache_hits_total` (counter) - labels: cache_tier
  - [ ] `acetone_cache_misses_total` (counter) - labels: cache_tier
  - [ ] `acetone_service_fabric_api_calls_total` (counter) - labels: operation
  - [ ] `acetone_service_fabric_api_duration_seconds` (histogram) - labels: operation
  - [ ] `acetone_circuit_breaker_state` (gauge) - labels: circuit_name
- [ ] Expose Prometheus `/metrics` endpoint
- [ ] Configure OpenTelemetry tracing:
  - [ ] Trace URL resolution flow
  - [ ] Include SF API calls as spans
  - [ ] Export to OTLP (configurable endpoint)
- [ ] Add metrics middleware to collect request metrics
- [ ] Integration tests for metrics endpoint

**Acceptance Criteria:**
- `/metrics` endpoint returns Prometheus format
- Custom Acetone metrics are exported
- YARP built-in metrics are included
- OpenTelemetry traces are emitted
- Metrics can be scraped by Prometheus

---

### Phase 3: Testing & Quality Assurance

#### Step 3.1: Unit Test Coverage
**Status:** `TODO`

**Tasks:**
- [ ] Achieve >90% code coverage for `Acetone.V2.Core`
- [ ] Achieve >80% code coverage for `Acetone.V2.Proxy`
- [ ] Create mock `FabricClient` for unit testing
- [ ] Port all test cases from V1 to V2
- [ ] Add new test cases for YARP integration
- [ ] Configure code coverage reporting in CI

**Acceptance Criteria:**
- Coverage meets thresholds
- All V1 scenarios are tested
- Tests run in <30 seconds

---

#### Step 3.2: Integration Tests
**Status:** `TODO`

**Tasks:**
- [ ] Create `Acetone.V2.IntegrationTests` project
- [ ] Set up WebApplicationFactory for in-memory testing
- [ ] Create integration tests:
  - [ ] End-to-end proxy flow (mock SF cluster)
  - [ ] Health check endpoints
  - [ ] Metrics endpoint
  - [ ] Error scenarios (SF unavailable, timeout, etc.)
  - [ ] Concurrent request handling
- [ ] Create optional real cluster tests:
  - [ ] Deploy test applications to real SF cluster
  - [ ] Validate resolution accuracy
  - [ ] Test cache behavior under load
  - [ ] Test service notification handling
- [ ] Add environment variable `ACETONE_V2_SKIP_REAL_CLUSTER_TESTS=1`

**Acceptance Criteria:**
- Integration tests pass consistently
- Real cluster tests work (when enabled)
- Tests validate full request/response cycle

---

#### Step 3.3: Performance Testing
**Status:** `TODO`

**Tasks:**
- [ ] Create performance test project using BenchmarkDotNet
- [ ] Benchmark scenarios:
  - [ ] URL parsing performance
  - [ ] Cache hit latency
  - [ ] Cache miss latency (with mock SF)
  - [ ] Concurrent request throughput
  - [ ] Memory allocation per request
- [ ] Create load test scripts:
  - [ ] Use `k6` or `wrk` for HTTP load testing
  - [ ] Test 1000 req/s with 10 concurrent connections
  - [ ] Measure P50, P95, P99 latencies
  - [ ] Monitor memory and CPU usage
- [ ] Compare V2 performance to V1 baseline
- [ ] Document performance characteristics

**Acceptance Criteria:**
- P95 latency <50ms for cache hits
- P95 latency <500ms for cache misses
- Throughput >5000 req/s on modern hardware
- Memory usage stable under sustained load

---

#### Step 3.4: Security Testing
**Status:** `TODO`

**Tasks:**
- [ ] Run static analysis (Roslyn analyzers)
- [ ] Run security scanning (Snyk, GitHub Advanced Security)
- [ ] Test certificate authentication modes:
  - [ ] CertificateThumbprint validation
  - [ ] CertificateCommonName validation
  - [ ] Certificate chain validation
  - [ ] Expired certificate handling
- [ ] Test HTTPS configuration:
  - [ ] TLS 1.2+ enforcement
  - [ ] Certificate validation
  - [ ] Cipher suite configuration
- [ ] Validate no sensitive data in logs
- [ ] Test header injection prevention
- [ ] Test path traversal prevention
- [ ] Document security considerations

**Acceptance Criteria:**
- No high/critical security findings
- All auth modes tested and validated
- HTTPS properly configured
- Security documentation complete

---

### Phase 4: Deployment & Operations

#### Step 4.1: Docker Containerization
**Status:** `TODO`

**Tasks:**
- [ ] Create multi-stage `Dockerfile`:
  ```dockerfile
  # Build stage
  FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
  WORKDIR /src
  COPY . .
  RUN dotnet restore
  RUN dotnet publish -c Release -o /app/publish

  # Runtime stage
  FROM mcr.microsoft.com/dotnet/aspnet:10.0
  WORKDIR /app
  COPY --from=build /app/publish .

  # Non-root user
  USER $APP_UID

  EXPOSE 8080 8081
  ENTRYPOINT ["dotnet", "Acetone.V2.Proxy.dll"]
  ```
- [ ] Create `.dockerignore` file
- [ ] Configure Kestrel to listen on port 8080 (HTTP) and 8081 (HTTPS)
- [ ] Add health check instruction to Dockerfile
- [ ] Test Docker image build
- [ ] Test Docker image run with environment variables
- [ ] Optimize image size (multi-stage build, minimal base image)
- [ ] Document Docker usage

**Acceptance Criteria:**
- Docker image builds successfully
- Image size <150 MB
- Container runs and serves traffic
- Health checks work in container

---

#### Step 4.2: Kubernetes Deployment
**Status:** `TODO`

**Tasks:**
- [ ] Create Kubernetes manifests:
  - [ ] `deployment.yaml` - Deployment with replicas, resource limits
  - [ ] `service.yaml` - ClusterIP or LoadBalancer service
  - [ ] `configmap.yaml` - Configuration data
  - [ ] `secret.yaml` - Certificate secrets (example only)
  - [ ] `ingress.yaml` - Ingress for external access (optional)
  - [ ] `servicemonitor.yaml` - Prometheus ServiceMonitor (if using Prometheus Operator)
- [ ] Configure resource limits:
  - [ ] CPU request: 100m, limit: 1000m
  - [ ] Memory request: 256Mi, limit: 512Mi
- [ ] Configure liveness/readiness probes:
  - [ ] Liveness: `/health/live`
  - [ ] Readiness: `/health/ready`
- [ ] Add pod anti-affinity for HA
- [ ] Add HorizontalPodAutoscaler (HPA) based on CPU/memory
- [ ] Test deployment on local Kubernetes (kind, minikube, or k3s)
- [ ] Document Kubernetes deployment

**Acceptance Criteria:**
- Manifests deploy successfully
- Pods pass health checks
- Service routes traffic to pods
- HPA scales based on load

---

#### Step 4.3: Helm Chart
**Status:** `TODO`

**Tasks:**
- [ ] Create Helm chart structure:
  ```
  helm/acetone-v2/
  ├── Chart.yaml
  ├── values.yaml
  ├── templates/
  │   ├── deployment.yaml
  │   ├── service.yaml
  │   ├── configmap.yaml
  │   ├── secret.yaml
  │   ├── ingress.yaml
  │   ├── servicemonitor.yaml
  │   └── hpa.yaml
  └── README.md
  ```
- [ ] Parameterize all configuration values
- [ ] Add default values for:
  - [ ] Replica count
  - [ ] Resource limits
  - [ ] Image repository and tag
  - [ ] Service type (ClusterIP/LoadBalancer)
  - [ ] Ingress configuration
  - [ ] Acetone-specific settings
- [ ] Add Helm chart documentation
- [ ] Test Helm install/upgrade/rollback
- [ ] Publish chart to repository (optional)

**Acceptance Criteria:**
- Chart installs successfully
- Values can be overridden
- Upgrade preserves state
- Documentation is clear

---

#### Step 4.4: CI/CD Pipeline
**Status:** `TODO`

**Tasks:**
- [ ] Create GitHub Actions workflow for V2:
  - [ ] Build all projects
  - [ ] Run unit tests
  - [ ] Run integration tests (mock cluster)
  - [ ] Generate code coverage report
  - [ ] Build Docker image
  - [ ] Push image to registry (Docker Hub, GHCR, ACR)
  - [ ] Run security scanning
  - [ ] Create GitHub release (on tag)
  - [ ] Publish NuGet packages (Core, SDK)
- [ ] Configure workflow triggers:
  - [ ] On push to main/develop
  - [ ] On pull requests
  - [ ] On version tags (v2.*)
- [ ] Add workflow status badges to README
- [ ] Configure branch protection rules
- [ ] Document CI/CD process

**Acceptance Criteria:**
- Workflow runs on all triggers
- Docker image is published
- NuGet packages are published
- Releases are automated

---

### Phase 5: Documentation & Migration

#### Step 5.1: Architecture Documentation
**Status:** `TODO`

**Tasks:**
- [ ] Create `docs/ARCHITECTURE.md`:
  - [ ] High-level architecture diagram
  - [ ] Component descriptions
  - [ ] Data flow diagrams
  - [ ] Sequence diagrams for URL resolution
  - [ ] Cache tier interactions
  - [ ] YARP integration points
- [ ] Document design decisions:
  - [ ] Why YARP over other proxies
  - [ ] Caching strategy rationale
  - [ ] Resilience pattern choices
  - [ ] Technology stack decisions
- [ ] Create API documentation:
  - [ ] XML doc comments for public APIs
  - [ ] Generate API docs with DocFX or similar

**Acceptance Criteria:**
- Architecture is clearly documented
- Diagrams are accurate and helpful
- Design decisions are explained
- API documentation is generated

---

#### Step 5.2: Configuration Documentation
**Status:** `TODO`

**Tasks:**
- [ ] Create `docs/CONFIGURATION.md`:
  - [ ] All configuration options with descriptions
  - [ ] Example configurations for common scenarios
  - [ ] Environment variable reference
  - [ ] Certificate configuration guide
  - [ ] Logging configuration
  - [ ] Metrics configuration
  - [ ] Health check configuration
- [ ] Create configuration schema (JSON Schema)
- [ ] Add IntelliSense support for appsettings.json

**Acceptance Criteria:**
- All options are documented
- Examples work out-of-the-box
- Schema validates configurations

---

#### Step 5.3: Deployment Documentation
**Status:** `TODO`

**Tasks:**
- [ ] Create `docs/DEPLOYMENT.md`:
  - [ ] Docker deployment guide
  - [ ] Kubernetes deployment guide
  - [ ] Helm deployment guide
  - [ ] Azure Container Apps deployment
  - [ ] AWS ECS deployment
  - [ ] Certificate management in containers
  - [ ] Environment-specific configurations
  - [ ] HA and scaling considerations
  - [ ] Monitoring and observability setup
  - [ ] Troubleshooting guide
- [ ] Create quickstart guides
- [ ] Create video tutorials (optional)

**Acceptance Criteria:**
- Users can deploy without assistance
- Common issues are documented
- Platform-specific guides are accurate

---

#### Step 5.4: Migration Guide
**Status:** `TODO`

**Tasks:**
- [ ] Create `docs/MIGRATION_GUIDE.md`:
  - [ ] V1 vs V2 feature comparison matrix
  - [ ] Breaking changes and workarounds
  - [ ] Configuration mapping (web.config → appsettings.json)
  - [ ] Step-by-step migration process
  - [ ] Coexistence strategies (run V1 and V2 side-by-side)
  - [ ] Testing migration before cutover
  - [ ] Rollback procedures
  - [ ] FAQ for common migration issues
- [ ] Create configuration converter tool (optional):
  - [ ] Parse web.config
  - [ ] Generate appsettings.json
  - [ ] Generate Kubernetes ConfigMap

**Acceptance Criteria:**
- Migration path is clear
- Breaking changes are documented
- Configuration converter works
- Users can migrate with confidence

---

#### Step 5.5: User Documentation
**Status:** `TODO`

**Tasks:**
- [ ] Update root `README.md`:
  - [ ] Overview of Acetone V1 and V2
  - [ ] Feature comparison
  - [ ] Links to documentation
  - [ ] Getting started guide
  - [ ] Contributing guidelines
- [ ] Create V2-specific `acetone-v2/README.md`:
  - [ ] Introduction
  - [ ] Quick start
  - [ ] Features
  - [ ] Usage examples
  - [ ] Links to detailed docs
- [ ] Create `CHANGELOG.md` for V2
- [ ] Create `CONTRIBUTING.md` for V2
- [ ] Update LICENSE if needed

**Acceptance Criteria:**
- README is clear and comprehensive
- Getting started is <5 minutes
- All docs are linked properly
- Changelog tracks versions

---

### Phase 6: Production Readiness

#### Step 6.1: Production Testing
**Status:** `TODO`

**Tasks:**
- [ ] Deploy V2 to staging environment
- [ ] Run smoke tests against staging
- [ ] Run load tests against staging
- [ ] Monitor metrics and logs
- [ ] Test certificate rotation
- [ ] Test configuration updates
- [ ] Test rolling updates
- [ ] Test disaster recovery scenarios
- [ ] Validate Service Fabric integration
- [ ] Collect feedback from beta users

**Acceptance Criteria:**
- Staging deployment is stable
- Performance meets targets
- No critical issues found
- Beta users are satisfied

---

#### Step 6.2: Observability Setup
**Status:** `TODO`

**Tasks:**
- [ ] Create Grafana dashboards:
  - [ ] Request rate, latency, error rate
  - [ ] Cache hit/miss ratios
  - [ ] Service Fabric API call metrics
  - [ ] Circuit breaker state
  - [ ] Resource utilization (CPU, memory)
- [ ] Create alerting rules:
  - [ ] High error rate (>5%)
  - [ ] High latency (P95 >1s)
  - [ ] Low cache hit rate (<80%)
  - [ ] Service Fabric connectivity issues
  - [ ] Circuit breaker open
  - [ ] Pod restart loops
- [ ] Configure log aggregation:
  - [ ] ELK, Splunk, or Loki
  - [ ] Structured log parsing
  - [ ] Correlation ID tracking
- [ ] Document observability setup

**Acceptance Criteria:**
- Dashboards show key metrics
- Alerts fire on issues
- Logs are searchable
- On-call runbook is complete

---

#### Step 6.3: Security Hardening
**Status:** `TODO`

**Tasks:**
- [ ] Enable security headers:
  - [ ] X-Content-Type-Options: nosniff
  - [ ] X-Frame-Options: DENY
  - [ ] Strict-Transport-Security (HSTS)
  - [ ] Content-Security-Policy
- [ ] Configure rate limiting:
  - [ ] Per-IP rate limits
  - [ ] Global rate limits
  - [ ] Integration with YARP rate limiting
- [ ] Enable request validation:
  - [ ] Path traversal prevention
  - [ ] Header injection prevention
  - [ ] Request size limits
- [ ] Configure TLS:
  - [ ] TLS 1.2+ only
  - [ ] Strong cipher suites
  - [ ] Certificate pinning (optional)
- [ ] Add security testing to CI
- [ ] Perform penetration testing
- [ ] Document security posture

**Acceptance Criteria:**
- Security headers are present
- Rate limiting is effective
- TLS is properly configured
- Penetration test passes

---

#### Step 6.4: Production Deployment
**Status:** `TODO`

**Tasks:**
- [ ] Create production deployment plan:
  - [ ] Phased rollout strategy (canary, blue-green)
  - [ ] Rollback plan
  - [ ] Communication plan
  - [ ] Incident response plan
- [ ] Deploy to production:
  - [ ] Start with canary (5% traffic)
  - [ ] Monitor metrics and errors
  - [ ] Gradually increase traffic (25%, 50%, 100%)
  - [ ] Validate functionality at each stage
- [ ] Decommission V1 (if applicable):
  - [ ] Drain traffic from V1
  - [ ] Archive V1 configuration
  - [ ] Remove V1 infrastructure
- [ ] Update documentation for production URLs
- [ ] Announce GA (General Availability)

**Acceptance Criteria:**
- Production deployment is successful
- No incidents during rollout
- V1 decommissioned (if planned)
- Users are notified

---

#### Step 6.5: Post-Launch Support
**Status:** `TODO`

**Tasks:**
- [ ] Monitor production metrics for 30 days
- [ ] Triage and fix production issues
- [ ] Collect user feedback
- [ ] Create issue templates for GitHub
- [ ] Set up support rotation (if applicable)
- [ ] Plan for future enhancements:
  - [ ] gRPC proxy support
  - [ ] WebSocket support
  - [ ] Advanced routing rules
  - [ ] Multi-cluster support
  - [ ] Configuration UI
- [ ] Document lessons learned
- [ ] Celebrate launch! 🎉

**Acceptance Criteria:**
- Production is stable
- Issues are triaged within 24h
- User feedback is positive
- Roadmap is defined

---

## Technical Considerations

### YARP Integration Approach

**Option A: Dynamic Route Provider (Recommended)**
- Implement `IProxyConfigProvider` to generate routes dynamically
- Use middleware to resolve SF endpoints before YARP proxying
- Pros: Full control, easy to customize, simple to debug
- Cons: More code to maintain

**Option B: Custom HttpTransformer**
- Use YARP's request transformation pipeline
- Implement `HttpTransformer` to resolve destinations
- Pros: Cleaner separation, leverages YARP features
- Cons: More complex integration

**Decision:** Option A (Dynamic Route Provider) for maximum flexibility and debuggability.

---

### Caching Strategy

**Preserve V1 Three-Tier Design:**
1. Application cache - long-lived, manual refresh
2. Service cache - event-driven invalidation
3. Partition cache - 30-second TTL

**Enhancements for V2:**
- Add distributed cache option (Redis) for multi-instance deployments
- Add cache warming endpoint (`POST /admin/warmup`)
- Add cache clear endpoint (`POST /admin/cache/clear`)
- Expose cache statistics via `/metrics`

---

### Configuration Philosophy

**Hierarchy (highest to lowest precedence):**
1. Command-line arguments
2. Environment variables (`ACETONE_*`)
3. `appsettings.{Environment}.json`
4. `appsettings.json`
5. Default values in code

**Environment Variable Naming:**
- Prefix: `ACETONE_`
- Nested: `ACETONE_ServiceFabric__ClusterConnectionStrings`
- Array: `ACETONE_ServiceFabric__ClusterConnectionStrings__0=node1:19000`

---

### Logging Strategy

**Use ASP.NET Core ILogger with:**
- Structured logging (JSON format)
- Semantic logging (LoggerMessage source generators)
- Log levels:
  - Trace: Detailed request/response bodies (dev only)
  - Debug: Cache operations, SF API calls
  - Information: URL resolutions, startup/shutdown
  - Warning: Retry attempts, cache misses
  - Error: Resolution failures, SF exceptions
  - Critical: Startup failures, unrecoverable errors

**Include in all logs:**
- Correlation ID
- Application name
- Request path
- User agent (optional)
- Timing information

---

### Metrics Strategy

**Follow Prometheus best practices:**
- Counter for totals (`_total` suffix)
- Histogram for durations (`_seconds` suffix)
- Gauge for current values
- Appropriate labels (avoid high cardinality)

**Key metrics:**
- Request rate by status code
- Request duration by endpoint
- Cache hit ratio by tier
- Service Fabric API call duration
- Circuit breaker state
- Active connections

---

### Security Considerations

**Certificate Management:**
- Support `X509Certificate2` loading from:
  - File path (PEM, PFX)
  - Certificate store (LocalMachine, CurrentUser)
  - Kubernetes secrets (mounted volume)
  - Azure Key Vault (via managed identity)
- Validate certificate expiry on startup
- Log warnings for certificates expiring <30 days

**Network Security:**
- Support mTLS for Service Fabric connections
- Support HTTPS for Kestrel listeners
- Support client certificate authentication (optional)
- Validate all hostnames against allowed patterns

---

### Performance Targets

**Latency:**
- P50 <10ms (cache hit)
- P95 <50ms (cache hit)
- P99 <100ms (cache hit)
- P95 <500ms (cache miss with SF call)

**Throughput:**
- >5000 req/s on 4-core, 8GB RAM
- Linear scaling with CPU cores

**Resource Usage:**
- <256MB memory baseline
- <512MB memory under load
- <50% CPU under 1000 req/s

**Cache Efficiency:**
- >95% hit rate in steady state
- <30s cache warmup time

---

## Success Criteria

### Functional Requirements
- [ ] All V1 features implemented in V2
- [ ] All V1 test scenarios pass in V2
- [ ] Configuration parity with V1
- [ ] Feature parity with V1

### Non-Functional Requirements
- [ ] Performance meets or exceeds V1
- [ ] Cross-platform deployment validated (Linux, Windows)
- [ ] Container deployment validated (Docker, Kubernetes)
- [ ] Security hardening complete
- [ ] Documentation complete and accurate
- [ ] CI/CD pipeline operational
- [ ] Production deployment successful

### Quality Gates
- [ ] >90% code coverage
- [ ] Zero critical security findings
- [ ] Zero P0 bugs in production
- [ ] <1% error rate in production
- [ ] >99.9% uptime in production

---

## Risks & Mitigations

### Risk: YARP API Changes
**Mitigation:** Pin YARP version, monitor releases, test before upgrading

### Risk: Service Fabric SDK Compatibility
**Mitigation:** Test with multiple SF runtime versions, document compatibility matrix

### Risk: Performance Regression vs V1
**Mitigation:** Benchmark early, optimize hot paths, use async/await properly

### Risk: Configuration Complexity
**Mitigation:** Provide clear examples, create configuration validator, add IntelliSense

### Risk: Migration Friction
**Mitigation:** Create detailed migration guide, provide side-by-side deployment option, offer support

---

## Timeline Estimate

| Phase | Estimated Duration | Dependencies |
|-------|-------------------|--------------|
| Phase 1: Foundation | 2-3 weeks | None |
| Phase 2: YARP Integration | 2-3 weeks | Phase 1 complete |
| Phase 3: Testing | 1-2 weeks | Phase 2 complete |
| Phase 4: Deployment | 1-2 weeks | Phase 3 complete |
| Phase 5: Documentation | 1 week | Phase 4 complete |
| Phase 6: Production | 2-4 weeks | Phase 5 complete |
| **Total** | **9-15 weeks** | Sequential |

**Parallel Work Opportunities:**
- Documentation can start during Phase 2-3
- Deployment artifacts can be created during Phase 3
- Security testing can run alongside Phase 3

---

## Open Questions

1. **Should V2 support HTTP/3 (QUIC)?**
   - Pro: Future-proof, better performance
   - Con: Complexity, limited client support
   - Decision: Not in initial release, consider for V2.1

2. **Should V2 support gRPC proxying?**
   - Pro: Modern protocol support
   - Con: Additional complexity
   - Decision: Not in initial release, plan for V2.2

3. **Should V2 have a configuration UI?**
   - Pro: Better UX for operators
   - Con: Significant scope increase
   - Decision: Not in initial release, evaluate post-GA

4. **Should V2 support multiple Service Fabric clusters?**
   - Pro: Multi-region, multi-environment support
   - Con: Routing complexity, configuration explosion
   - Decision: Not in initial release, evaluate based on demand

5. **Should V2 use .NET 10 or wait for .NET 11?**
   - Pro (.NET 10): Stable LTS, available now
   - Pro (.NET 11): Newer features, longer support
   - Decision: Use .NET 10 (current LTS), migrate to .NET 11 when available

---

## Next Steps

1. Review and approve this plan
2. Create GitHub project board with all tasks
3. Assign owners to phases
4. Set up development environment
5. Begin Phase 1.1: Project Setup

---

**Document Version:** 1.0
**Last Updated:** 2025-11-15
**Author:** Claude (Anthropic)
**Status:** Draft - Awaiting Approval
