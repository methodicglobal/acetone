using System.Net;
using Xunit;

namespace Acetone.V2.Proxy.Tests.Integration;

public class MetricsIntegrationTests : IClassFixture<AcetoneProxyApplicationFactory>
{
    private readonly AcetoneProxyApplicationFactory _factory;

    public MetricsIntegrationTests(AcetoneProxyApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Metrics_ReturnsPrometheusFormat()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8; version=0.0.4", response.Content.Headers.ContentType?.ToString());

        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(content));
    }
}
