using FluentAssertions;
using Acetone.V2.Proxy.Metrics;

namespace Acetone.V2.Proxy.Tests.Metrics;

public class AcetoneMetricsTests
{
    [Fact]
    public void IncrementUrlResolutions_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.IncrementUrlResolutions("success");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordUrlResolutionDuration_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.RecordUrlResolutionDuration(0.123);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementCacheHits_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.IncrementCacheHits();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementCacheMisses_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.IncrementCacheMisses();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementServiceFabricApiCalls_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.IncrementServiceFabricApiCalls("GetServiceEndpoint");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordServiceFabricApiDuration_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.RecordServiceFabricApiDuration("GetServiceEndpoint", 0.456);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void SetCircuitBreakerState_ShouldNotThrow()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act
        Action act = () => metrics.SetCircuitBreakerState("serviceName", "Open");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleMetricCalls_ShouldWorkCorrectly()
    {
        // Arrange
        var metrics = new AcetoneMetrics();

        // Act & Assert
        metrics.IncrementUrlResolutions("success");
        metrics.RecordUrlResolutionDuration(0.1);
        metrics.IncrementCacheHits();
        metrics.IncrementCacheMisses();
        metrics.IncrementServiceFabricApiCalls("GetService");
        metrics.RecordServiceFabricApiDuration("GetService", 0.2);
        metrics.SetCircuitBreakerState("test-service", "Closed");
    }
}
