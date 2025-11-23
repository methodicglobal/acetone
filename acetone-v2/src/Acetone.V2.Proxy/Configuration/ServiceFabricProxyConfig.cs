using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Configuration;

public class ServiceFabricProxyConfig : IProxyConfig
{
    public ServiceFabricProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
        Routes = routes;
        Clusters = clusters;
        ChangeToken = new CancellationChangeToken(CancellationToken.None); // Static config for now
    }

    public IReadOnlyList<RouteConfig> Routes { get; }

    public IReadOnlyList<ClusterConfig> Clusters { get; }

    public IChangeToken ChangeToken { get; }
}
