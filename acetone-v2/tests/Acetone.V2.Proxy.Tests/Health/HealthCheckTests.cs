using Acetone.V2.Core.ServiceFabric;
using Acetone.V2.Proxy.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace Acetone.V2.Proxy.Tests.Health;

public class HealthCheckTests
{
    [Fact]
    public async Task ServiceFabricHealthCheck_ReturnsHealthy_WhenResolverIsAvailable()
    {
        // Arrange
        var resolver = Substitute.For<IServiceFabricResolver>();
        var check = new ServiceFabricHealthCheck(resolver);
        var context = new HealthCheckContext();

        // Act
        var result = await check.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
