using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Acetone.V2.Proxy.HealthChecks;

namespace Acetone.V2.Proxy.Tests.HealthChecks;

public class CacheHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthyStatus()
    {
        // Arrange
        var healthCheck = new CacheHealthCheck();
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesCacheStatistics_InData()
    {
        // Arrange
        var healthCheck = new CacheHealthCheck();
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Data.Should().NotBeNull();
        result.Data.Should().ContainKey("cacheSize");
    }

    [Fact]
    public async Task CheckHealthAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var healthCheck = new CacheHealthCheck();
        var context = new HealthCheckContext();
        using var cts = new CancellationTokenSource();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, cts.Token);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesDescription_InResult()
    {
        // Arrange
        var healthCheck = new CacheHealthCheck();
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Description.Should().NotBeNullOrEmpty();
    }
}
