using Acetone.V2.Proxy.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Tests.Configuration;

public class ServiceFabricProxyConfigProviderTests
{
    private readonly ServiceFabricProxyConfigProvider _provider;

    public ServiceFabricProxyConfigProviderTests()
    {
        _provider = new ServiceFabricProxyConfigProvider();
    }

    [Fact]
    public void GetConfig_ReturnsCatchAllRoute()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        Assert.NotNull(config);
        Assert.Single(config.Routes);
        
        var route = config.Routes[0];
        Assert.Equal("ServiceFabricCatchAllRoute", route.RouteId);
        Assert.Equal("ServiceFabricCluster", route.ClusterId);
        Assert.Equal("{**catch-all}", route.Match.Path);
    }

    [Fact]
    public void GetConfig_ReturnsServiceFabricCluster()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        Assert.NotNull(config);
        Assert.Single(config.Clusters);
        
        var cluster = config.Clusters[0];
        Assert.Equal("ServiceFabricCluster", cluster.ClusterId);
        // We don't need destinations here as we'll resolve dynamically, 
        // but YARP might require at least an empty map or specific config.
        // For now, let's assume we just need the cluster definition.
    }

    [Fact]
    public void GetConfig_ChangeToken_IsActive()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        Assert.NotNull(config.ChangeToken);
        Assert.False(config.ChangeToken.HasChanged);
    }
}
