using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Acetone.V2.Proxy.Yarp;

namespace Acetone.V2.Proxy.Tests.Yarp;

public class ServiceFabricProxyConfigFilterTests
{
    [Fact]
    public void ConfigureCluster_ShouldNotModifyCluster()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureCluster(cluster, default);

        // Assert
        result.Should().NotBeNull();
        result.ClusterId.Should().Be("test-cluster");
    }

    [Fact]
    public void ConfigureRoute_ShouldAddRequestTransforms()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        result.Should().NotBeNull();
        result.Transforms.Should().NotBeNull();
        result.Transforms.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void ConfigureRoute_ShouldAddXForwardedHeaders()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Check for X-Forwarded headers
        transforms.Should().Contain(t =>
            t.ContainsKey("RequestHeaderOriginalHost") ||
            t.ContainsKey("X-Forwarded"));
    }

    [Fact]
    public void ConfigureRoute_ShouldAddCorrelationIdHeader()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Should have transform for X-Correlation-Id header
        transforms.Should().Contain(t =>
            t.TryGetValue("RequestHeader", out var value) &&
            value?.ToString() == "X-Correlation-Id");
    }

    [Fact]
    public void ConfigureRoute_ShouldAddAcetoneVersionHeader()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Should have transform for X-Acetone-Version header
        transforms.Should().Contain(t =>
            t.TryGetValue("RequestHeader", out var value) &&
            value?.ToString() == "X-Acetone-Version");
    }

    [Fact]
    public void ConfigureRoute_ShouldRemoveSensitiveHeaders()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Should have transforms to remove sensitive headers
        // Looking for transforms that remove Authorization or other sensitive headers
        transforms.Should().Contain(t =>
            t.TryGetValue("RequestHeaderRemove", out var _));
    }

    [Fact]
    public void ConfigureRoute_ShouldAddResponseTransforms()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Should have response header transforms
        transforms.Should().Contain(t =>
            t.TryGetValue("ResponseHeader", out var _));
    }

    [Fact]
    public void ConfigureRoute_ShouldAddResolutionTimeHeader()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Should have transform for X-Acetone-Resolution-Time-Ms header
        transforms.Should().Contain(t =>
            t.TryGetValue("ResponseHeader", out var value) &&
            value?.ToString() == "X-Acetone-Resolution-Time-Ms");
    }

    [Fact]
    public void ConfigureRoute_ShouldAddCacheHitHeader()
    {
        // Arrange
        var filter = new ServiceFabricProxyConfigFilter();
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "test-cluster",
            Match = new RouteMatch { Path = "/test" }
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "test-cluster"
        };

        // Act
        var result = filter.ConfigureRoute(route, cluster, default);

        // Assert
        var transforms = result.Transforms?.ToList();
        transforms.Should().NotBeNull();

        // Should have transform for X-Acetone-Cache-Hit header
        transforms.Should().Contain(t =>
            t.TryGetValue("ResponseHeader", out var value) &&
            value?.ToString() == "X-Acetone-Cache-Hit");
    }
}
