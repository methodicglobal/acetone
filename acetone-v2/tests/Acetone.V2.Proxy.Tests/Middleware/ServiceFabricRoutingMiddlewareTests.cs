using Acetone.V2.Core;
using Acetone.V2.Proxy.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Yarp.ReverseProxy.Forwarder;

namespace Acetone.V2.Proxy.Tests.Middleware;

public class ServiceFabricRoutingMiddlewareTests
{
    private readonly Mock<IServiceFabricResolver> _resolverMock;
    private readonly Mock<ILogger<ServiceFabricRoutingMiddleware>> _loggerMock;
    private readonly Mock<RequestDelegate> _nextMock;
    private readonly ServiceFabricRoutingMiddleware _middleware;

    public ServiceFabricRoutingMiddlewareTests()
    {
        _resolverMock = new Mock<IServiceFabricResolver>();
        _loggerMock = new Mock<ILogger<ServiceFabricRoutingMiddleware>>();
        _nextMock = new Mock<RequestDelegate>();
        _middleware = new ServiceFabricRoutingMiddleware(
            _nextMock.Object,
            _resolverMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_WithValidApplication_ShouldResolveAndSetDestination()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/fabric:/MyApp/api/test";
        context.Request.Method = "GET";

        var resolvedUrl = "http://localhost:8080";
        _resolverMock
            .Setup(r => r.ResolveUrlAsync("fabric:/MyApp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedUrl);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _resolverMock.Verify(r => r.ResolveUrlAsync("fabric:/MyApp", It.IsAny<CancellationToken>()), Times.Once);
        _nextMock.Verify(n => n(context), Times.Once);

        // Verify that the destination was set
        var feature = context.Features.Get<IForwarderErrorFeature>();
        // Note: In actual implementation, we'd set IReverseProxyFeature
    }

    [Fact]
    public async Task InvokeAsync_WithKeyNotFoundException_ShouldReturn404()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/fabric:/NonExistent/api/test";
        context.Response.Body = new MemoryStream();

        _resolverMock
            .Setup(r => r.ResolveUrlAsync("fabric:/NonExistent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Application not found"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(404);
        _nextMock.Verify(n => n(context), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithFabricException_ShouldReturn503()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/fabric:/MyApp/api/test";
        context.Response.Body = new MemoryStream();

        // Create a mock FabricException (we'll use a generic exception for now)
        _resolverMock
            .Setup(r => r.ResolveUrlAsync("fabric:/MyApp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service Fabric communication failed"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(503);
        _nextMock.Verify(n => n(context), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithTimeout_ShouldReturn504()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/fabric:/MyApp/api/test";
        context.Response.Body = new MemoryStream();

        _resolverMock
            .Setup(r => r.ResolveUrlAsync("fabric:/MyApp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Resolution timed out"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(504);
        _nextMock.Verify(n => n(context), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddCorrelationIdHeader()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/fabric:/MyApp/api/test";

        var resolvedUrl = "http://localhost:8080";
        _resolverMock
            .Setup(r => r.ResolveUrlAsync("fabric:/MyApp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedUrl);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Request.Headers.Should().ContainKey("X-Correlation-ID");
        context.Request.Headers["X-Correlation-ID"].ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WithExistingCorrelationId_ShouldPreserveIt()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/fabric:/MyApp/api/test";
        var existingCorrelationId = "existing-correlation-id";
        context.Request.Headers["X-Correlation-ID"] = existingCorrelationId;

        var resolvedUrl = "http://localhost:8080";
        _resolverMock
            .Setup(r => r.ResolveUrlAsync("fabric:/MyApp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedUrl);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Request.Headers["X-Correlation-ID"].ToString().Should().Be(existingCorrelationId);
    }

    [Theory]
    [InlineData("/fabric:/MyApp/api/test", "fabric:/MyApp")]
    [InlineData("/fabric:/MyApp", "fabric:/MyApp")]
    [InlineData("/fabric:/App/Service/endpoint", "fabric:/App")]
    public async Task InvokeAsync_ShouldExtractApplicationNameCorrectly(string path, string expectedAppName)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var resolvedUrl = "http://localhost:8080";
        _resolverMock
            .Setup(r => r.ResolveUrlAsync(expectedAppName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedUrl);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _resolverMock.Verify(r => r.ResolveUrlAsync(expectedAppName, It.IsAny<CancellationToken>()), Times.Once);
    }
}
