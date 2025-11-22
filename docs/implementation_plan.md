# Phase 4: Deployment & Operations Plan

## Goal Description
Implement robust deployment and operations infrastructure for Acetone V2, ensuring it is ready for a fintech production environment. This includes containerization, Kubernetes orchestration, and automated CI/CD pipelines.

## User Review Required
- Review Docker base image selection (using Chiseled Ubuntu for security).
- Review Kubernetes resource limits and security context.
- Review CI/CD triggers and secrets requirements.

## Proposed Changes

### Step 4.1: Docker Containerization
#### [NEW] [Dockerfile](file:///Users/timbrian/repos/acetone-1/acetone-v2/src/Acetone.V2.Proxy/Dockerfile)
- Multi-stage build.
- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (Secure, minimal, non-root).
- Build image: `mcr.microsoft.com/dotnet/sdk:10.0-noble`.
- Expose ports 8080 (HTTP) and 9090 (Metrics).
- Health check instruction? (Docker healthcheck or rely on orchestrator).

#### [NEW] [.dockerignore](file:///Users/timbrian/repos/acetone-1/acetone-v2/.dockerignore)
- Exclude `bin`, `obj`, `.git`, `tests`, etc.

### Step 4.2: Kubernetes Deployment
#### [NEW] [charts/acetone-proxy/Chart.yaml](file:///Users/timbrian/repos/acetone-1/acetone-v2/charts/acetone-proxy/Chart.yaml)
- Helm chart definition.

#### [NEW] [charts/acetone-proxy/values.yaml](file:///Users/timbrian/repos/acetone-1/acetone-v2/charts/acetone-proxy/values.yaml)
- Default configuration (replicas, resources, image, config).

#### [NEW] [charts/acetone-proxy/templates/deployment.yaml](file:///Users/timbrian/repos/acetone-1/acetone-v2/charts/acetone-proxy/templates/deployment.yaml)
- Deployment resource.
- SecurityContext (runAsNonRoot, readOnlyRootFilesystem).
- Liveness/Readiness probes pointing to `/health/live` and `/health/ready`.
- Resources (Requests/Limits).

#### [NEW] [charts/acetone-proxy/templates/service.yaml](file:///Users/timbrian/repos/acetone-1/acetone-v2/charts/acetone-proxy/templates/service.yaml)
- Service definition (ClusterIP).

#### [NEW] [charts/acetone-proxy/templates/configmap.yaml](file:///Users/timbrian/repos/acetone-1/acetone-v2/charts/acetone-proxy/templates/configmap.yaml)
- `appsettings.json` override.

### Step 4.3: CI/CD Pipeline
#### [NEW] [.github/workflows/ci.yaml](file:///Users/timbrian/repos/acetone-1/acetone-v2/.github/workflows/ci.yaml)
- Triggers: Push to main, Pull Request.
- Jobs:
    - `build-and-test`: Restore, Build, Test (Unit, Integration, Security).
    - `docker-build`: Build Docker image (skip push for PRs).
    - `security-scan`: Run `dotnet list package --vulnerable`.

### Step 4.4: Documentation
#### [MODIFY] [README.md](file:///Users/timbrian/repos/acetone-1/README.md)
- Add "Deployment" section.
- Add "Monitoring" section.

## Verification Plan

### Automated Tests
- CI pipeline will verify build and tests.
- `docker build` verification.
- `helm lint` verification.

### Manual Verification
- Local Docker run: `docker run -p 8080:8080 acetone-proxy`.
- Verify `/health/live` endpoint in container.
