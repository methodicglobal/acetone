using Acetone.V2.Core;
using Acetone.V2.Proxy.Yarp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Tests.Yarp;

public class ServiceFabricProxyConfigProviderTests
{
    private readonly Mock<ILogger<ServiceFabricProxyConfigProvider>> _loggerMock;
    private readonly ServiceFabricProxyConfigProvider _provider;

    public ServiceFabricProxyConfigProviderTests()
    {
        _loggerMock = new Mock<ILogger<ServiceFabricProxyConfigProvider>>();
        _provider = new ServiceFabricProxyConfigProvider(_loggerMock.Object);
    }

    [Fact]
    public void GetConfig_ShouldReturnValidConfiguration()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        config.Should().NotBeNull();
        config.Routes.Should().NotBeNull();
        config.Clusters.Should().NotBeNull();
    }

    [Fact]
    public void GetConfig_ShouldCreateCatchAllRoute()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        config.Routes.Should().ContainSingle();
        var route = config.Routes.First();
        route.RouteId.Should().Be("service-fabric-catch-all");
        route.Match.Should().NotBeNull();
        route.Match.Path.Should().Be("{**catch-all}");
    }

    [Fact]
    public void GetConfig_ShouldCreateDefaultCluster()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        config.Clusters.Should().ContainSingle();
        var cluster = config.Clusters.First();
        cluster.ClusterId.Should().Be("service-fabric-cluster");
    }

    [Fact]
    public void GetConfig_ShouldConfigureRoundRobinLoadBalancing()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        var cluster = config.Clusters.First();
        cluster.LoadBalancingPolicy.Should().Be("RoundRobin");
    }

    [Fact]
    public void GetConfig_ShouldConfigurePassiveHealthChecks()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        var cluster = config.Clusters.First();
        cluster.HealthCheck.Should().NotBeNull();
        cluster.HealthCheck!.Passive.Should().NotBeNull();
        cluster.HealthCheck.Passive!.Enabled.Should().BeTrue();
        cluster.HealthCheck.Passive.Policy.Should().Be("TransportFailureRate");
    }

    [Fact]
    public void GetConfig_ShouldIncludeCorrelationMetadata()
    {
        // Act
        var config = _provider.GetConfig();

        // Assert
        var route = config.Routes.First();
        route.Metadata.Should().NotBeNull();
        route.Metadata.Should().ContainKey("correlation-enabled");
        route.Metadata!["correlation-enabled"].Should().Be("true");
    }

    [Fact]
    public void GetConfig_ShouldReturnSameConfigurationOnMultipleCalls()
    {
        // Act
        var config1 = _provider.GetConfig();
        var config2 = _provider.GetConfig();

        // Assert
        config1.Should().BeSameAs(config2);
    }

    [Fact]
    public void GetChangeToken_ShouldReturnValidToken()
    {
        // Act
        var token = _provider.GetChangeToken();

        // Assert
        token.Should().NotBeNull();
        token.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void Update_ShouldTriggerChangeToken()
    {
        // Arrange
        var token1 = _provider.GetChangeToken();
        var changeDetected = false;
        token1.RegisterChangeCallback(_ => changeDetected = true, null);

        // Act
        _provider.Update();
        var token2 = _provider.GetChangeToken();

        // Assert
        changeDetected.Should().BeTrue();
        token1.HasChanged.Should().BeTrue();
        token2.Should().NotBeSameAs(token1);
        token2.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void Update_ShouldInvalidateConfigurationCache()
    {
        // Arrange
        var config1 = _provider.GetConfig();

        // Act
        _provider.Update();
        var config2 = _provider.GetConfig();

        // Assert
        config1.Should().NotBeSameAs(config2);
    }
}
