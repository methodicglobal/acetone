using System.Net;
using Xunit;

namespace Acetone.V2.Proxy.Tests.Integration;

public class HealthCheckIntegrationTests : IClassFixture<AcetoneProxyApplicationFactory>
{
    private readonly AcetoneProxyApplicationFactory _factory;

    public HealthCheckIntegrationTests(AcetoneProxyApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Live_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public async Task Get_Ready_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }
}
