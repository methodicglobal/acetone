using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Acetone.V2.Proxy.Configuration;

public class ServiceFabricProxyConfigProvider : IProxyConfigProvider
{
    public IProxyConfig GetConfig()
    {
        var route = new RouteConfig
        {
            RouteId = "ServiceFabricCatchAllRoute",
            ClusterId = "ServiceFabricCluster",
            Match = new RouteMatch
            {
                Path = "{**catch-all}"
            }
        };

        var cluster = new ClusterConfig
        {
            ClusterId = "ServiceFabricCluster",
            LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        };

        return new ServiceFabricProxyConfig(new[] { route }, new[] { cluster });
    }
}
