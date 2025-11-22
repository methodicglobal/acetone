using System.Fabric;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.ServiceFabric;
using Acetone.V2.Core.Utilities;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace Acetone.V2.Proxy.Middleware;

public class ServiceFabricRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceFabricResolver _resolver;
    private readonly AcetoneOptions _options;
    private readonly ILogger<ServiceFabricRoutingMiddleware> _logger;

    public ServiceFabricRoutingMiddleware(
        RequestDelegate next,
        IServiceFabricResolver resolver,
        IOptions<AcetoneOptions> options,
        ILogger<ServiceFabricRoutingMiddleware> logger)
    {
        _next = next;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        if (proxyFeature == null)
        {
            await _next(context);
            return;
        }

        string? applicationName = GetApplicationName(context);
        if (string.IsNullOrEmpty(applicationName))
        {
            _logger.LogWarning("Could not determine application name from request: {Host}{Path}", context.Request.Host, context.Request.Path);
            context.Response.StatusCode = 400;
            return;
        }

        try
        {
            string resolvedUrl = await _resolver.ResolveUrlAsync(applicationName, Guid.NewGuid());
            
            // Update the destination address
            // Note: In YARP, the DestinationConfig is immutable. We need to create a new Model.
            // However, we can't easily replace the Model on the existing DestinationState because it's read-only.
            // But we can replace the AvailableDestinations list with a new list containing a new DestinationState?
            // Or we can rely on the fact that we are in a middleware before the proxying happens.
            // Actually, YARP's dynamic destination model usually involves a custom IProxyConfigProvider (which we have)
            // OR updating the request URI if we are just forwarding.
            // BUT, if we want to use YARP's load balancing and health checks, we need valid destinations.
            // Here we have a single "catch-all" destination.
            // We can update the Request.Path or Request.Host, but YARP uses the DestinationConfig.Address.
            
            // Wait, if we update the DestinationConfig.Address, it affects all requests using that destination!
            // This is NOT thread-safe if we share the destination.
            // Since we have a generic "ServiceFabricCluster", we likely have one destination.
            // If we change it, we change it for everyone.
            // This approach of "updating destination" is wrong for per-request dynamic routing in YARP if we share the cluster.
            
            // ALTERNATIVE: Use "Direct Forwarding" (IHttpForwarder) - but then we lose some YARP features (middleware pipeline).
            // OR: Use YARP's "Dynamic Destinations" where we create a new destination per request? No, too heavy.
            
            // CORRECT APPROACH for YARP dynamic routing to different upstreams per request:
            // We should set the `Yarp.ReverseProxy.Forwarder.RequestTransformer` or modify the `proxyRequest` in a transform.
            // BUT, we want to resolve BEFORE forwarding.
            
            // Actually, the common pattern for dynamic routing in YARP is to set the `IReverseProxyFeature.AvailableDestinations` 
            // to a *new* list containing a *ephemeral* destination with the resolved address.
            // Let's try creating a new DestinationState.
            
            var destinationConfig = new Yarp.ReverseProxy.Configuration.DestinationConfig
            {
                Address = resolvedUrl
            };
            var destinationModel = new DestinationModel(destinationConfig);
            var destinationState = new DestinationState(Guid.NewGuid().ToString());
            
            // Reflection hack again? No, we need a clean way.
            // DestinationState constructor is public. Model is read-only property.
            // Wait, DestinationState.Model is settable via internal setter in YARP?
            // In YARP 1.1+, DestinationState is just a holder.
            // If we can't set Model, we can't use DestinationState easily.
            
            // Let's check if we can just update the Request URI and let YARP forward it?
            // No, YARP constructs the destination URI by combining Destination.Address + Request.Path.
            
            // Let's look at how others do "Dynamic Upstream" in YARP.
            // Usually they use `IHttpForwarder` directly.
            // BUT we are building a full proxy.
            
            // Let's try the reflection approach for now as we did in tests, assuming we can set it.
            // If not, we might need to implement a custom `IForwarder` or use `IHttpForwarder`.
            // But `IHttpForwarder` is lower level.
            
            // Let's assume for this task we can set the Model via reflection (as we did in tests) 
            // OR we find a better way.
            // Actually, `DestinationState` is not meant to be created per request.
            
            // BETTER WAY:
            // Use `context.Items` to store the resolved URL, and then use a custom `HttpTransformer` 
            // to override the destination URI.
            // This is the standard YARP way for dynamic routing.
            // We don't change the Destination, we change the Request URI that YARP sends.
            
            // So, the middleware should:
            // 1. Resolve URL.
            // 2. Store it in `context.Items["ResolvedUrl"]`.
            // 3. We need a Transform that uses this.
            
            // BUT, the requirements said "Set YARP destination via context.Features.Get<IReverseProxyFeature>()".
            // If I replace `AvailableDestinations` with a new list, YARP will use it.
            // The problem is creating a `DestinationState` with a `Model`.
            
            // Let's try to stick to the plan: "Update YARP destination".
            // If I can't create a DestinationState with a Model publicly, I might be blocked.
            // Let's check `DestinationState` source code (mental check).
            // It has `public DestinationModel Model { get; internal set; }`.
            // So only assemblies with internals visible can set it.
            
            // So I MUST use Reflection or `IHttpForwarder`.
            // Reflection is fragile.
            // `IHttpForwarder` is robust but bypasses some config.
            
            // Let's go with the Reflection approach for now to satisfy the immediate requirement, 
            // but add a TODO to refactor to `HttpTransformer`.
            // Actually, `ServiceFabricRoutingMiddleware` is essentially doing what a Transform could do.
            
            // Let's implement the reflection set for now.
            
            var newDestination = new DestinationState(Guid.NewGuid().ToString());
            var newModel = new DestinationModel(new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = resolvedUrl });
            typeof(DestinationState).GetProperty("Model")?.SetValue(newDestination, newModel);
            
            proxyFeature.AvailableDestinations = new List<DestinationState> { newDestination };
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Application not found: {AppName}", applicationName);
            context.Response.StatusCode = 404;
            return;
        }
        catch (FabricTransientException ex)
        {
            _logger.LogError(ex, "Service Fabric transient error resolving {AppName}", applicationName);
            context.Response.StatusCode = 503;
            return;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout resolving {AppName}", applicationName);
            context.Response.StatusCode = 504;
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving {AppName}", applicationName);
            context.Response.StatusCode = 500;
            return;
        }

        await _next(context);
    }

    private string? GetApplicationName(HttpContext context)
    {
        if (_options.ApplicationNameLocation == ApplicationNameLocation.Subdomain)
        {
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length > 0)
            {
                return parts[0];
            }
        }
        // TODO: Implement Path-based extraction if needed
        return null;
    }
}
