using Acetone.V2.Proxy.Yarp;
using FluentAssertions;
using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Tests.Yarp;

public class DynamicRouteBuilderTests
{
    [Fact]
    public void BuildCatchAllRoute_ShouldCreateValidRoute()
    {
        // Act
        var route = DynamicRouteBuilder.BuildCatchAllRoute();

        // Assert
        route.Should().NotBeNull();
        route.RouteId.Should().Be("service-fabric-catch-all");
        route.ClusterId.Should().Be("service-fabric-cluster");
    }

    [Fact]
    public void BuildCatchAllRoute_ShouldUseCatchAllPattern()
    {
        // Act
        var route = DynamicRouteBuilder.BuildCatchAllRoute();

        // Assert
        route.Match.Should().NotBeNull();
        route.Match.Path.Should().Be("{**catch-all}");
    }

    [Fact]
    public void BuildCatchAllRoute_ShouldIncludeMetadata()
    {
        // Act
        var route = DynamicRouteBuilder.BuildCatchAllRoute();

        // Assert
        route.Metadata.Should().NotBeNull();
        route.Metadata.Should().ContainKey("correlation-enabled");
        route.Metadata!["correlation-enabled"].Should().Be("true");
    }

    [Fact]
    public void BuildCatchAllRoute_ShouldSetOrder()
    {
        // Act
        var route = DynamicRouteBuilder.BuildCatchAllRoute();

        // Assert
        route.Order.Should().Be(int.MaxValue);
    }

    [Fact]
    public void BuildDefaultCluster_ShouldCreateValidCluster()
    {
        // Act
        var cluster = DynamicRouteBuilder.BuildDefaultCluster();

        // Assert
        cluster.Should().NotBeNull();
        cluster.ClusterId.Should().Be("service-fabric-cluster");
    }

    [Fact]
    public void BuildDefaultCluster_ShouldConfigureRoundRobinLoadBalancing()
    {
        // Act
        var cluster = DynamicRouteBuilder.BuildDefaultCluster();

        // Assert
        cluster.LoadBalancingPolicy.Should().Be("RoundRobin");
    }

    [Fact]
    public void BuildDefaultCluster_ShouldConfigurePassiveHealthChecks()
    {
        // Act
        var cluster = DynamicRouteBuilder.BuildDefaultCluster();

        // Assert
        cluster.HealthCheck.Should().NotBeNull();
        cluster.HealthCheck!.Passive.Should().NotBeNull();
        cluster.HealthCheck.Passive!.Enabled.Should().BeTrue();
        cluster.HealthCheck.Passive.Policy.Should().Be("TransportFailureRate");
    }

    [Fact]
    public void BuildDefaultCluster_ShouldHaveEmptyDestinations()
    {
        // Act
        var cluster = DynamicRouteBuilder.BuildDefaultCluster();

        // Assert
        cluster.Destinations.Should().NotBeNull();
        cluster.Destinations.Should().BeEmpty();
    }

    [Fact]
    public void BuildDefaultCluster_ShouldSetReactivationPeriod()
    {
        // Act
        var cluster = DynamicRouteBuilder.BuildDefaultCluster();

        // Assert
        cluster.HealthCheck!.Passive!.ReactivationPeriod.Should().Be(TimeSpan.FromSeconds(30));
    }
}
