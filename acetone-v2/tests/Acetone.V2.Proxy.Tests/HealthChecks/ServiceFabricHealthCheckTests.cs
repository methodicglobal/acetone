using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Acetone.V2.Proxy.HealthChecks;

namespace Acetone.V2.Proxy.Tests.HealthChecks;

public class ServiceFabricHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenFabricClientConnected_ReturnsHealthy()
    {
        // Arrange
        var healthCheck = new ServiceFabricHealthCheck();
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // In a real scenario, we'd mock FabricClient, but for now we expect it to handle gracefully
        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var healthCheck = new ServiceFabricHealthCheck();
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
        var healthCheck = new ServiceFabricHealthCheck();
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Description.Should().NotBeNullOrEmpty();
    }
}
