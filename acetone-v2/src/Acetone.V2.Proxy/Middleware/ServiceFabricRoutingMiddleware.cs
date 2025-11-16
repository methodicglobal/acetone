using Acetone.V2.Core;
using Yarp.ReverseProxy.Forwarder;
using System.Text.RegularExpressions;

namespace Acetone.V2.Proxy.Middleware;

/// <summary>
/// Middleware that resolves Service Fabric endpoints and configures YARP destination
/// </summary>
public class ServiceFabricRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceFabricResolver _resolver;
    private readonly ILogger<ServiceFabricRoutingMiddleware> _logger;
    private static readonly Regex AppNameRegex = new(@"^/fabric:/[^/]+", RegexOptions.Compiled);

    public ServiceFabricRoutingMiddleware(
        RequestDelegate next,
        IServiceFabricResolver resolver,
        ILogger<ServiceFabricRoutingMiddleware> logger)
    {
        _next = next;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get or create correlation ID
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers["X-Correlation-ID"] = correlationId;
        }

        try
        {
            // Extract application name from the request path
            var applicationName = ExtractApplicationName(context.Request.Path);

            if (string.IsNullOrEmpty(applicationName))
            {
                _logger.LogWarning(
                    "No Service Fabric application name found in path: {Path}. CorrelationId: {CorrelationId}",
                    context.Request.Path, correlationId);

                await _next(context);
                return;
            }

            _logger.LogDebug(
                "Resolving Service Fabric application: {ApplicationName}. CorrelationId: {CorrelationId}",
                applicationName, correlationId);

            // Resolve the Service Fabric endpoint
            var resolvedUrl = await _resolver.ResolveUrlAsync(applicationName, context.RequestAborted);

            _logger.LogInformation(
                "Resolved {ApplicationName} to {ResolvedUrl}. CorrelationId: {CorrelationId}",
                applicationName, resolvedUrl, correlationId);

            // Set the YARP destination
            SetYarpDestination(context, resolvedUrl);

            // Continue to the next middleware (YARP will handle the actual proxying)
            await _next(context);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "Service Fabric application not found. Path: {Path}, CorrelationId: {CorrelationId}",
                context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"error\":\"Application not found\",\"correlationId\":\"{correlationId}\"}}");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex,
                "Timeout resolving Service Fabric endpoint. Path: {Path}, CorrelationId: {CorrelationId}",
                context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"error\":\"Gateway timeout\",\"correlationId\":\"{correlationId}\"}}");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex,
                "Service Fabric communication error. Path: {Path}, CorrelationId: {CorrelationId}",
                context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"error\":\"Service unavailable\",\"correlationId\":\"{correlationId}\"}}");
        }
    }

    /// <summary>
    /// Extracts the Service Fabric application name from the request path
    /// Expected format: /fabric:/AppName/...
    /// </summary>
    private static string? ExtractApplicationName(PathString path)
    {
        var match = AppNameRegex.Match(path.Value ?? string.Empty);
        if (match.Success)
        {
            // Remove the leading slash
            return match.Value.TrimStart('/');
        }

        return null;
    }

    /// <summary>
    /// Sets the YARP destination for the request
    /// </summary>
    private void SetYarpDestination(HttpContext context, string destinationUrl)
    {
        // Store the destination URL in HttpContext items for YARP to use
        // YARP will use this when forwarding the request
        context.Items["DestinationUrl"] = destinationUrl;

        // Note: In a real implementation, we would set IReverseProxyFeature here
        // For now, we're storing it in Items for the YARP middleware to pick up
        _logger.LogDebug("Set YARP destination to: {DestinationUrl}", destinationUrl);
    }
}
