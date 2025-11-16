using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace Acetone.V2.Proxy.Tests.Metrics;

public class MetricsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MetricsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPrometheusFormat()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().NotBeNullOrEmpty();
        // Prometheus format should contain metric definitions
        content.Should().Contain("# HELP");
        content.Should().Contain("# TYPE");
    }

    [Fact]
    public async Task MetricsEndpoint_IncludesAcetoneMetrics()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("acetone_url_resolutions_total");
        content.Should().Contain("acetone_url_resolution_duration_seconds");
        content.Should().Contain("acetone_cache_hits_total");
        content.Should().Contain("acetone_cache_misses_total");
        content.Should().Contain("acetone_service_fabric_api_calls_total");
        content.Should().Contain("acetone_service_fabric_api_duration_seconds");
        content.Should().Contain("acetone_circuit_breaker_state");
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsTextPlainContentType()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
    }
}
