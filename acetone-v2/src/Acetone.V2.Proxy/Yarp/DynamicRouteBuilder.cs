using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Yarp;

/// <summary>
/// Builds YARP route and cluster configurations dynamically
/// </summary>
public static class DynamicRouteBuilder
{
    /// <summary>
    /// Builds a catch-all route that matches all incoming requests
    /// </summary>
    public static RouteConfig BuildCatchAllRoute()
    {
        return new RouteConfig
        {
            RouteId = "service-fabric-catch-all",
            ClusterId = "service-fabric-cluster",
            Match = new RouteMatch
            {
                Path = "{**catch-all}"
            },
            Order = int.MaxValue,
            Metadata = new Dictionary<string, string>
            {
                { "correlation-enabled", "true" }
            }
        };
    }

    /// <summary>
    /// Builds the default Service Fabric cluster configuration
    /// </summary>
    public static ClusterConfig BuildDefaultCluster()
    {
        return new ClusterConfig
        {
            ClusterId = "service-fabric-cluster",
            LoadBalancingPolicy = "RoundRobin",
            Destinations = new Dictionary<string, DestinationConfig>(),
            HealthCheck = new HealthCheckConfig
            {
                Passive = new PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = "TransportFailureRate",
                    ReactivationPeriod = TimeSpan.FromSeconds(30)
                }
            }
        };
    }
}
