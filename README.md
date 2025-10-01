# Methodic Acetone – Service Fabric URL Resolver for IIS

Acetone is a high-performance IIS Rewrite Provider that seamlessly routes HTTP requests to **Service Fabric** services without requiring hardcoded endpoints. It automatically discovers services, resolves partitions, and caches results for optimal performance.

---

## Features

- **Automatic Service Discovery**: Query Service Fabric clusters to find running services
- **Dynamic Endpoint Resolution**: Resolve partition endpoints dynamically
- **Intelligent Caching**: Cache application and service metadata for performance
- **Multiple Routing Modes**: Support various URL-to-service mapping strategies
- **Pull Request Routing**: Automatically route PR-specific URLs to dedicated service instances
- **Certificate Authentication**: Secure cluster communication with X.509 certificates
- **High Performance**: Built-in caching and optimized for high-throughput scenarios

---

## Quick Start

1. Install Acetone on your IIS server
2. Configure Service Fabric cluster connection
3. Set up certificate authentication
4. Configure URL rewrite rules
5. Proceed to configuration.

---

## Configuration

### General Settings

#### Cluster Connection String (`ClusterConnectionString`) – **Required**
The connection string for the Service Fabric cluster.  
Can include multiple comma-separated endpoints for HA/failover.

Example:
```
https://my-cluster-ss-lb.methodic.com:66042
```

#### Application Name Location (`ApplicationNameLocation`) – Default: `Subdomain`
Defines how the service name is extracted from the request:

- `Subdomain`: `https://mycoolservice.methodic.com`
- `SubdomainPostHyphens`: `https://uat-mycoolservice.methodic.com`
- `SubdomainPreHyphens`: `https://mycoolservice-uat.methodic.com`
- `FirstUrlFragment`: `https://uat.methodic.com/mycoolservice`

If omitted or invalid → defaults to `Subdomain`.

**Pull Request Routing**: When using `Subdomain` or `FirstUrlFragment` modes, URLs with the pattern `{serviceName}-{pullRequestId}` are automatically routed to Service Fabric applications named `{ServiceName}-PR{pullRequestId}`.

Examples:
- `https://guard-12906.pav.meth.wtf` → routes to `Guard-PR12906` application
- `https://api.methodic.com/service-999` → routes to `Service-PR999` application

#### Partition Cache Limit (`PartitionCacheLimit`) – Default: `5`
Max number of cached partition endpoint entries.

#### Log Information (`LogInformation`) – Default: `False`
If `True`, logs informational messages to Windows Event Log.

---

### Credentials (X.509 Certificates)

Only X.509 certificate-based auth is currently supported.

#### **Thumbprint-based**
- `CredentialsType` = `CertificateThumbprint`
- `ClientCertificateThumbprint` → Thumbprint in `LocalMachine.My` store.
- `ServerCertificateThumbprint` → Thumbprint of Service Fabric cluster cert.

#### **Common Name-based**
- `CredentialsType` = `CertificateCommonName`
- `ClientCertificateSubjectDistinguishedName` = e.g. `CN=Methodic Global`
- `ClientCertificateIssuerDistinguishedName` = Full issuer DN.
- `ServerCertificateCommonNames` (optional) = Comma-separated CN list.

---

### Future Features
- `VersionParameter`: Query string key holding app version → used for version-specific routing.
- `ClearCacheParameter`: Query string key to force cache refresh (diagnostics).

---

## Pull Request Routing

Acetone automatically detects and routes pull request-specific URLs to dedicated Service Fabric application instances. This enables seamless testing of feature branches without manual configuration.

### How It Works

1. **URL Pattern Detection**: Automatically detects URLs matching `{serviceName}-{pullRequestId}`
2. **Application Name Transformation**: Converts to Service Fabric application name `{ServiceName}-PR{pullRequestId}`
3. **Automatic Routing**: Routes requests to the correct PR-specific application instance

### Supported URL Formats

| URL | Application Name | Description |
|-----|------------------|-------------|
| `https://guard-12906.pav.meth.wtf` | `Guard-PR12906` | Subdomain mode |
| `https://api.methodic.com/guard-12906` | `Guard-PR12906` | FirstUrlFragment mode |
| `https://service-999.dev.company.com` | `Service-PR999` | Any numeric PR ID |

### Service Fabric Application Naming

Deploy your PR-specific applications using the naming convention:
```
{ServiceName}-PR{PullRequestId}
```

Examples:
- `Guard-PR12906`
- `Api-PR1234` 
- `MyService-PR999`

### Error Handling

- If a PR-specific application doesn't exist, Acetone throws a `KeyNotFoundException`
- No fallback to production services ensures isolation
- Regular URLs without PR patterns continue to work normally

---

## Maintenance

### Logging
If `LogInformation` is true, Acetone writes to **Windows Event Log** under its own source.

### Example IIS URL Rewrite Rule
```xml
<rewrite>
  <rules>
    <rule name="ReverseProxyInboundRule1" stopProcessing="true">
      <match url="(.*)" />
      <conditions>
        <add input="{ACETONE:{CACHE_URL}}" pattern="(.+):\/\/(.+):(\d+)" />
      </conditions>
      <action type="Rewrite" url="{C:1}://{SERVER_NAME}:{C:3}{URL}" appendQueryString="true" logRewrittenUrl="true" />
    </rule>
  </rules>
  <outboundRules>
    <rule name="ReverseProxyOutboundRule1" preCondition="ResponseIsHtml1" enabled="true">
      <match filterByTags="A, Form, Img" serverVariable="RESPONSE_Location" pattern="https:\/\/([\w.]+)(:)(\d\d\d\d\d?)(.*)?" />
      <action type="Rewrite" value="https://{R:1}:443{R:4}" replace="true" />
    </rule>
    <preConditions>
      <preCondition name="ResponseIsHtml1">
        <add input="{RESPONSE_STATUS}" pattern="3\d\d" />
      </preCondition>
    </preConditions>
  </outboundRules>
</rewrite>
```

---

## Example Scenarios

### 1. Multi-environment routing
- `uat-mycoolservice.methodic.com` → UAT instance
- `prod-mycoolservice.methodic.com` → Production instance

### 2. Pull Request routing
- `mycoolservice-12906.methodic.com` → PR #12906 instance
- `mycoolservice.methodic.com` → Production instance

### 3. Partitioned services
Partition-based routing based on Service Fabric partition key.

### 4. Cache refresh
Request with `?no-cache=true` bypasses endpoint cache.

---

## Troubleshooting

| Problem | Likely Cause | Solution |
|---------|--------------|----------|
| Requests bypass Acetone | Rewrite rule misconfigured | Check `{ACETONE:{CACHE_URL}}` condition |
| 500 Internal Server Error | Cert permissions issue | Ensure IIS AppPool user has private key access |
| Cluster not found | Wrong connection string | Verify port, DNS, and firewall |
| PR app not found | Application not deployed | Deploy Service Fabric app with correct naming: `{ServiceName}-PR{PRId}` |
| VersionParam ignored | Feature not yet implemented | Wait for future release |

---

## Building from Source
```powershell
nuget restore Methodic.Acetone.sln
msbuild Methodic.Acetone.sln /p:Configuration=Release
```

---

## License
MIT License. See `LICENSE` file.
