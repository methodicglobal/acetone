using System.Fabric;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.ServiceFabric;
using Acetone.V2.Proxy.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Yarp.ReverseProxy.Model;

namespace Acetone.V2.Proxy.Tests.Middleware;

public class ServiceFabricRoutingMiddlewareTests
{
    private readonly RequestDelegate _next;
    private readonly IServiceFabricResolver _resolver;
    private readonly IOptions<AcetoneOptions> _options;
    private readonly ILogger<ServiceFabricRoutingMiddleware> _logger;
    private readonly ServiceFabricRoutingMiddleware _middleware;

    public ServiceFabricRoutingMiddlewareTests()
    {
        _next = Substitute.For<RequestDelegate>();
        _resolver = Substitute.For<IServiceFabricResolver>();
        _options = Options.Create(new AcetoneOptions());
        _logger = Substitute.For<ILogger<ServiceFabricRoutingMiddleware>>();
        _middleware = new ServiceFabricRoutingMiddleware(_next, _resolver, _options, _logger);
    }

    [Fact]
    public async Task InvokeAsync_ResolvesAndSetsDestination_WhenAppFound()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("myapp.example.com");
        
        // Mock YARP feature
        var proxyFeature = Substitute.For<IReverseProxyFeature>();
        var destination = new DestinationState("dest1");
        var model = new DestinationModel(new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = "http://placeholder" });
        
        // Use reflection to set the Model property backing field or internal setter if possible, 
        // but DestinationState constructor doesn't take Model.
        // Actually, DestinationState is mutable via its Model property setter? No, error says read-only.
        // Let's check YARP source or use a helper.
        // DestinationState has a public constructor taking id.
        // Model is a property.
        // Wait, YARP 2.x DestinationState might be different.
        // Let's try to use reflection to set the private field if it exists, or just create a wrapper if needed.
        // Actually, for unit testing YARP middleware, we often mock the context features.
        // But DestinationState is a concrete class.
        
        // Let's try reflection.
        typeof(DestinationState).GetProperty("Model")?.SetValue(destination, model);
        
        proxyFeature.AvailableDestinations.Returns(new List<DestinationState> { destination });
        context.Features.Set(proxyFeature);

        _resolver.ResolveUrlAsync("myapp", Arg.Any<Guid>()).Returns(Task.FromResult("http://10.0.0.1:8080/"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        // Assert
        var updatedDestination = proxyFeature.AvailableDestinations[0];
        Assert.Equal("http://10.0.0.1:8080/", updatedDestination.Model.Config.Address);
        await _next.Received(1).Invoke(context);
    }

    [Fact]
    public async Task InvokeAsync_Returns404_WhenAppNotFound()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("unknown.example.com");
        
        var proxyFeature = Substitute.For<IReverseProxyFeature>();
        proxyFeature.AvailableDestinations.Returns(new List<DestinationState> { new DestinationState("dest1") });
        context.Features.Set(proxyFeature);

        _resolver.ResolveUrlAsync("unknown", Arg.Any<Guid>()).Returns(Task.FromException<string>(new KeyNotFoundException()));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(404, context.Response.StatusCode);
        await _next.DidNotReceive().Invoke(context);
    }
    [Fact]
    public async Task InvokeAsync_Returns503_WhenFabricTransientException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("transient.example.com");
        
        var proxyFeature = Substitute.For<IReverseProxyFeature>();
        proxyFeature.AvailableDestinations.Returns(new List<DestinationState> { new DestinationState("dest1") });
        context.Features.Set(proxyFeature);

        _resolver.ResolveUrlAsync("transient", Arg.Any<Guid>()).Returns(Task.FromException<string>(new FabricTransientException()));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(503, context.Response.StatusCode);
        await _next.DidNotReceive().Invoke(context);
    }

    [Fact]
    public async Task InvokeAsync_Returns504_WhenTimeoutException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("timeout.example.com");
        
        var proxyFeature = Substitute.For<IReverseProxyFeature>();
        proxyFeature.AvailableDestinations.Returns(new List<DestinationState> { new DestinationState("dest1") });
        context.Features.Set(proxyFeature);

        _resolver.ResolveUrlAsync("timeout", Arg.Any<Guid>()).Returns(Task.FromException<string>(new TimeoutException()));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(504, context.Response.StatusCode);
        await _next.DidNotReceive().Invoke(context);
    }

    [Fact]
    public async Task InvokeAsync_Returns500_WhenGenericException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("error.example.com");
        
        var proxyFeature = Substitute.For<IReverseProxyFeature>();
        proxyFeature.AvailableDestinations.Returns(new List<DestinationState> { new DestinationState("dest1") });
        context.Features.Set(proxyFeature);

        _resolver.ResolveUrlAsync("error", Arg.Any<Guid>()).Returns(Task.FromException<string>(new Exception("Generic error")));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        await _next.DidNotReceive().Invoke(context);
    }
}
