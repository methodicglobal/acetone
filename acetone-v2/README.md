# Acetone V2 - Cross-Platform Service Fabric Reverse Proxy

[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![YARP](https://img.shields.io/badge/YARP-2.2-green)](https://microsoft.github.io/reverse-proxy/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](../LICENSE)

## Overview

**Acetone V2** is a modern, cross-platform reverse proxy built on **.NET 10**, **Kestrel**, and **YARP (Yet Another Reverse Proxy)** for **Azure Service Fabric** environments. It provides dynamic service discovery and intelligent routing for Service Fabric applications with enterprise-grade resilience, caching, and observability.

This is a complete rewrite of Acetone V1, moving from IIS/.NET Framework 4.8.1 to a cloud-native, containerized architecture that runs on Linux, Windows, and macOS.

## Key Features

### 🚀 Core Capabilities
- **Dynamic Service Discovery** - Automatically resolves Service Fabric application endpoints
- **Pull Request Routing** - Transforms `service-1234` URLs to `Service-PR1234` application names
- **Multiple URL Modes** - Subdomain, FirstUrlFragment, SubdomainPreHyphens, SubdomainPostHyphens
- **Three-Tier Caching** - Application (persistent), Service (event-driven), Partition (30s TTL)
- **Cross-Platform** - Runs on Linux, Windows, macOS, Docker, Kubernetes

### 🛡️ Resilience & Reliability
- **Exponential Backoff Retry** - 10 attempts with configurable delays (100ms → 2s)
- **Circuit Breaker** - Prevents cascading failures with half-open state recovery
- **Per-Attempt Timeouts** - 5-second timeout per Service Fabric API call
- **Health Checks** - `/health`, `/health/ready`, `/health/live` endpoints for Kubernetes
- **Graceful Degradation** - Continues operation during transient failures

### 📊 Observability
- **Prometheus Metrics** - `/metrics` endpoint with custom Acetone metrics
- **OpenTelemetry Tracing** - Distributed tracing for full request flow
- **Structured Logging** - JSON logs with correlation IDs
- **Custom Metrics**:
  - `acetone_url_resolutions_total` - Resolution counts by status
  - `acetone_url_resolution_duration_seconds` - Resolution latency histogram
  - `acetone_cache_hits_total` / `acetone_cache_misses_total` - Cache performance
  - `acetone_service_fabric_api_calls_total` - SF API call counts
  - `acetone_circuit_breaker_state` - Circuit breaker status

### 🔒 Security
- **Three Authentication Modes**:
  - Local (unsecured - development only)
  - Certificate Thumbprint
  - Certificate Common Name (with issuer validation)
- **X.509 Certificate Support** - Full certificate chain validation
- **Secure Headers** - X-Forwarded-*, X-Correlation-Id, sensitive header removal
- **HTTPS Support** - TLS 1.2+ with Kestrel

## Architecture

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
       ▼
┌─────────────────────────────────────┐
│      Kestrel Web Server             │
│  (HTTP/1.1, HTTP/2, HTTPS)          │
└──────────────┬──────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  Exception Handling Middleware       │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  Service Fabric Routing Middleware   │
│  • Extract app name from URL         │
│  • Resolve SF endpoint (with cache)  │
│  • Set YARP destination              │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  YARP Reverse Proxy                  │
│  • Dynamic configuration provider    │
│  • Load balancing (round-robin)      │
│  • Health checks (passive)           │
│  • Request/response transforms       │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  Service Fabric Application          │
│  (Resolved endpoint)                 │
└──────────────────────────────────────┘
```

## Project Structure

```
acetone-v2/
├── src/
│   ├── Acetone.V2.Core/                    # Core library (portable)
│   │   ├── Configuration/                  # Options & validation
│   │   ├── ServiceFabric/                  # SF resolver
│   │   ├── Caching/                        # Three-tier cache
│   │   ├── Resilience/                     # Retry & circuit breaker
│   │   └── ServiceFabricUrlParser.cs       # URL parsing logic
│   │
│   └── Acetone.V2.Proxy/                   # YARP reverse proxy
│       ├── Middleware/                     # SF routing & exceptions
│       ├── Yarp/                           # Config provider & transforms
│       ├── HealthChecks/                   # Health check implementations
│       ├── Metrics/                        # Prometheus metrics
│       └── Program.cs                      # Application entry point
│
├── tests/
│   ├── Acetone.V2.Core.Tests/              # Unit tests for Core
│   ├── Acetone.V2.Proxy.Tests/             # Unit tests for Proxy
│   └── Acetone.V2.IntegrationTests/        # Integration tests
│
└── docs/
    ├── CONFIGURATION.md                    # Configuration guide
    ├── DEPLOYMENT.md                       # Deployment instructions
    └── MIGRATION_GUIDE.md                  # V1 → V2 migration
```

## Quick Start

### Prerequisites
- .NET 10 SDK ([download](https://dotnet.microsoft.com/download/dotnet/10.0))
- Azure Service Fabric cluster (local or cloud)
- X.509 certificate (for secure clusters)

### Installation

```bash
# Clone the repository
git clone https://github.com/methodicglobal/acetone.git
cd acetone/acetone-v2

# Restore packages
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Configuration

Create `appsettings.json`:

```json
{
  "Acetone": {
    "ServiceFabricConnectionString": "localhost:19000",
    "EnableDetailedLogging": true,
    "MaxConcurrentRequests": 100,
    "Resilience": {
      "RetryCount": 10,
      "InitialRetryDelayMs": 100,
      "MaxRetryDelayMs": 2000,
      "PerAttemptTimeoutMs": 5000,
      "CircuitBreakerFailureThreshold": 5,
      "CircuitBreakerBreakDurationMs": 30000
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      },
      "Https": {
        "Url": "https://localhost:5001"
      }
    }
  }
}
```

### Running the Proxy

```bash
cd src/Acetone.V2.Proxy
dotnet run
```

The proxy will start on:
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`
- Health: `http://localhost:5000/health`
- Metrics: `http://localhost:5000/metrics`

### Docker Deployment

```bash
# Build Docker image
docker build -t acetone-v2:latest .

# Run container
docker run -p 5000:8080 -p 5001:8081 \
  -e ACETONE__ServiceFabricConnectionString="mycluster:19000" \
  acetone-v2:latest
```

### Kubernetes Deployment

```bash
# Apply manifests
kubectl apply -f deployment/kubernetes/

# Check status
kubectl get pods -l app=acetone-v2
kubectl get svc acetone-v2
```

## Testing

### Unit Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate coverage report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport"
```

### Integration Tests

```bash
# Run integration tests (requires SF cluster)
dotnet test --filter "Category=Integration"
```

### Performance Tests

```bash
cd tests/Acetone.V2.PerformanceTests
dotnet run -c Release
```

## Configuration Reference

### Application Name Location Modes

| Mode | Example Input | Extracted App Name |
|------|---------------|-------------------|
| **Subdomain** | `myservice.company.com` | `myservice` |
| **SubdomainPreHyphens** | `myservice-uat-01.company.com` | `myservice` |
| **SubdomainPostHyphens** | `uat-01-myservice.company.com` | `myservice` |
| **FirstUrlFragment** | `api.company.com/myservice/endpoint` | `myservice` |

### Pull Request Pattern

- Input: `service-1234.company.com`
- Transformed to: `Service-PR1234` (application name)
- First character capitalized, rest lowercased

### Cache Configuration

```json
{
  "Acetone": {
    "Resilience": {
      "PartitionCacheTtlSeconds": 30,
      "DisablePartitionCache": false
    }
  }
}
```

### Authentication

#### Local (Unsecured)
```json
{
  "Acetone": {
    "ServiceFabricConnectionString": "localhost:19000"
  }
}
```

#### Certificate Thumbprint
```json
{
  "Acetone": {
    "ServiceFabricConnectionString": "mycluster:19000",
    "ClientCertificateThumbprint": "ABCDEF1234567890",
    "ServerCertificateThumbprints": ["1234567890ABCDEF"]
  }
}
```

#### Certificate Common Name
```json
{
  "Acetone": {
    "ServiceFabricConnectionString": "mycluster:19000",
    "ClientCertificateCommonName": "CN=MyClient",
    "ClientCertificateIssuerDistinguishedName": "CN=MyCA",
    "ServerCertificateCommonNames": ["MyclusterCert"]
  }
}
```

## Monitoring

### Prometheus Metrics

```bash
# Scrape metrics
curl http://localhost:5000/metrics

# Example metrics
acetone_url_resolutions_total{status="success"} 1250
acetone_url_resolution_duration_seconds_bucket{le="0.1"} 1200
acetone_cache_hits_total 1180
acetone_cache_misses_total 70
acetone_circuit_breaker_state{service="ServiceFabric",state="Closed"} 0
```

### Health Checks

```bash
# Liveness probe
curl http://localhost:5000/health/live

# Readiness probe (checks SF connectivity)
curl http://localhost:5000/health/ready

# Full health check
curl http://localhost:5000/health
```

## Performance

### Benchmarks

- **P50 Latency (cache hit)**: <10ms
- **P95 Latency (cache hit)**: <50ms
- **P99 Latency (cache hit)**: <100ms
- **P95 Latency (cache miss)**: <500ms
- **Throughput**: >5000 req/s on 4-core, 8GB RAM
- **Memory Usage**: <256MB baseline, <512MB under load
- **Cache Hit Rate**: >95% in steady state

## Migration from V1

See [MIGRATION_GUIDE.md](docs/MIGRATION_GUIDE.md) for detailed migration instructions.

**Key Differences:**

| Aspect | V1 (IIS) | V2 (YARP) |
|--------|----------|-----------|
| Web Server | IIS | Kestrel |
| Framework | .NET Framework 4.8.1 | .NET 10 |
| Platform | Windows only | Cross-platform |
| Configuration | web.config | appsettings.json |
| Logging | Windows Event Log | Structured logging |
| Metrics | None | Prometheus |
| Health Checks | None | ASP.NET Core health checks |

## Troubleshooting

### Common Issues

**1. Cannot connect to Service Fabric cluster**
- Check connection string: `localhost:19000` (local) or `mycluster.region.cloudapp.azure.com:19000` (cloud)
- Verify certificate configuration
- Check firewall rules

**2. High cache miss rate**
- Increase `PartitionCacheTtlSeconds` (default: 30)
- Check Service Fabric topology stability
- Review application naming patterns

**3. Circuit breaker constantly opening**
- Check Service Fabric cluster health
- Increase `CircuitBreakerFailureThreshold` (default: 5)
- Increase `PerAttemptTimeoutMs` (default: 5000)

**4. 404 errors for valid applications**
- Verify application name pattern matches `ApplicationNameLocation` mode
- Check Service Fabric application deployment
- Review logs for name extraction: `dotnet run --environment Development`

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

### Development Setup

```bash
# Install .NET 10 SDK
# Clone repository
git clone https://github.com/methodicglobal/acetone.git
cd acetone/acetone-v2

# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Run proxy locally
cd src/Acetone.V2.Proxy
dotnet run --environment Development
```

## License

MIT License - see [LICENSE](../LICENSE) for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/methodicglobal/acetone/issues)
- **Discussions**: [GitHub Discussions](https://github.com/methodicglobal/acetone/discussions)
- **Documentation**: [docs/](docs/)

## Roadmap

### V2.1 (Planned)
- gRPC proxy support
- WebSocket support
- Distributed caching (Redis)
- Configuration UI

### V2.2 (Planned)
- Advanced routing rules (header-based, query-based)
- Multi-cluster support
- Rate limiting per application
- Request/response modification policies

## Acknowledgments

Built with:
- [YARP](https://microsoft.github.io/reverse-proxy/) - Microsoft's reverse proxy toolkit
- [Polly](https://github.com/App-vNext/Polly) - Resilience and transient fault handling
- [prometheus-net](https://github.com/prometheus-net/prometheus-net) - Prometheus metrics
- [OpenTelemetry](https://opentelemetry.io/) - Distributed tracing

---

**Acetone V2** - Production-ready Service Fabric reverse proxy for cloud-native environments.
