using Acetone.V2.Core.ServiceFabric;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Acetone.V2.Proxy.Health;

public class ServiceFabricHealthCheck : IHealthCheck
{
    private readonly IServiceFabricResolver _resolver;

    public ServiceFabricHealthCheck(IServiceFabricResolver resolver)
    {
        _resolver = resolver;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // For now, we just check if the resolver is initialized.
        // In a real scenario, we might want to ping the cluster or check a system service.
        if (_resolver != null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Service Fabric Resolver is available."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Service Fabric Resolver is null."));
    }
}
