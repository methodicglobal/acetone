using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Acetone.V2.Proxy.Configuration;

public class ServiceFabricProxyConfigFilter : IProxyConfigFilter
{
    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
    {
        return new ValueTask<ClusterConfig>(cluster);
    }

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        // Return a new RouteConfig with updated transforms
        return new ValueTask<RouteConfig>(route);
    }
}
