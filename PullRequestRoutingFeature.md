# Pull Request URL Routing Feature

## Overview
This feature adds support for routing URLs with pull request identifiers to specific Service Fabric application instances.

## URL Pattern
The system now recognizes URLs with the pattern: `{serviceName}-{pullRequestId}.{domain}`

### Examples:
- `https://guard-12906.pav.meth.wtf` ? routes to Service Fabric application `Guard-PR12906`
- `https://api-1234.test.methodic.com` ? routes to Service Fabric application `Api-PR1234`
- `https://api.methodic.com/service-999` ? routes to Service Fabric application `Service-PR999` (when using FirstUrlFragment mode)

## How It Works

### URL Transformation
1. **Input URL**: `https://guard-12906.pav.meth.wtf`
2. **Pattern Detection**: Regex `^(.+)-(\d+)$` matches `guard-12906`
3. **Extraction**: 
   - Service Name: `guard`
   - PR Number: `12906`
4. **Transformation**: `Guard-PR12906` (capitalized service name + "PR" + number)

### Application Name Location Modes
The PR pattern detection works with:
- **Subdomain**: `https://guard-12906.pav.meth.wtf` 
- **FirstUrlFragment**: `https://api.methodic.com/guard-12906`

It does NOT apply to:
- **SubdomainPreHyphens**: Would extract `guard` (before first hyphen)
- **SubdomainPostHyphens**: Would extract `12906` (after last hyphen)

### Fallback Behavior
- If the PR-specific application (`Guard-PR12906`) doesn't exist, the system throws a `KeyNotFoundException`
- Regular URLs without the PR pattern continue to work as before:
  - `https://guard.pav.meth.wtf` ? `guard`
  - `https://my-service.methodic.com` ? `my-service` (hyphen present but no numeric suffix)

## Implementation Details

### Code Changes
1. **Modified `TryGetApplicationNameFromUrl` method** in `ServiceFabricUrlResolver.cs`
   - Added regex pattern detection for `{name}-{digits}` format
   - Added transformation logic to create `{Name}-PR{digits}` format
   - Only applies to Subdomain and FirstUrlFragment modes

2. **Added comprehensive tests** in `AcetoneUnitTests.cs` and `PullRequestUrlTests.cs`
   - Tests for various PR URL patterns
   - Tests for regular URL compatibility
   - Tests for different application name location modes

### Service Fabric Application Naming
Applications should be deployed with names following the pattern:
- `{ServiceName}-PR{PullRequestId}`
- Examples: `Guard-PR12906`, `Api-PR1234`, `Service-PR999`

### Port Assignment
Service Fabric automatically assigns ports to each application instance. The Acetone system will discover and route to the correct port automatically through the Service Fabric client APIs.

## Configuration
No additional configuration is required. The feature works with existing `ApplicationNameLocation` settings:
- Set to `Subdomain` for subdomain-based PR routing
- Set to `FirstUrlFragment` for path-based PR routing

## Error Handling
- If a PR-specific application is not found, a `KeyNotFoundException` is thrown with details about the missing application
- This ensures that requests only route to deployed PR instances, preventing accidental fallback to production services