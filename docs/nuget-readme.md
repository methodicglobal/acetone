# Methodic Acetone

Methodic Acetone is a fintech-grade Service Fabric URL resolver and IIS rewrite provider. It converts user-friendly hostnames (including pull-request preview domains) into the live Service Fabric service endpoints that power your reverse proxy or edge routing tier.

## Highlights

- **Deterministic discovery** – enumerates applications, services, and partitions with clear error semantics.
- **Pull-request awareness** – automatically normalises names such as `service-1234` to `Service-PR1234`.
- **Resilient caching** – tiered cache strategy with optional invalidation to keep latency low under load.
- **Pure library** – drop the assembly into IIS URL Rewrite or reference it from other .NET Framework 4.8.1 workloads.

## Getting Started

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

See the full project documentation on [GitHub](https://github.com/methodicglobal/acetone) for advanced configuration, pull-request routing behaviour, and integration test guidance.
