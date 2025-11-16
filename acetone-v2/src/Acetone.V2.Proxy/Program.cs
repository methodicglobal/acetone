using Acetone.V2.Core;
using Acetone.V2.Proxy.HealthChecks;
using Acetone.V2.Proxy.Metrics;
using Acetone.V2.Proxy.Middleware;
using Acetone.V2.Proxy.Yarp;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel
builder.WebHost.ConfigureKestrel((context, options) =>
{
    var config = context.Configuration.GetSection("Kestrel");

    // HTTP endpoint
    options.Listen(IPAddress.Any, 5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // HTTPS endpoint
    options.Listen(IPAddress.Any, 5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        listenOptions.UseHttps();
    });

    // Connection limits
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxConcurrentUpgradedConnections = 1000;

    // Request limits
    options.Limits.MaxRequestBodySize = 52428800; // 50MB
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);

    // HTTP/2 settings
    options.Limits.Http2.MaxStreamsPerConnection = 100;
    options.Limits.Http2.HeaderTableSize = 4096;
    options.Limits.Http2.MaxFrameSize = 16384;
    options.Limits.Http2.MaxRequestHeaderFieldSize = 8192;
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// Add structured logging
builder.Services.AddLogging(logging =>
{
    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    logging.AddFilter("Yarp", LogLevel.Information);
    logging.AddFilter("Acetone", LogLevel.Debug);
});

// Configure ServiceFabricResolver options
builder.Services.Configure<ServiceFabricResolverOptions>(
    builder.Configuration.GetSection("ServiceFabric"));

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceFabricHealthCheck>("service_fabric", tags: new[] { "ready" })
    .AddCheck<CacheHealthCheck>("cache", tags: new[] { "ready" });

// Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromMemory(Array.Empty<Yarp.ReverseProxy.Configuration.RouteConfig>(),
                    Array.Empty<Yarp.ReverseProxy.Configuration.ClusterConfig>())
    .AddConfigFilter<ServiceFabricProxyConfigFilter>();

// Register custom services
// Note: IServiceFabricResolver should be registered by the hosting application
// For now, we'll add it as a placeholder that can be overridden
builder.Services.AddSingleton<ServiceFabricProxyConfigProvider>();

// Add Metrics
builder.Services.AddSingleton<AcetoneMetrics>();

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Acetone.V2.Proxy"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    });

var app = builder.Build();

// Configure middleware pipeline
app.UseRouting();

// Add exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Add Service Fabric routing middleware
app.UseMiddleware<ServiceFabricRoutingMiddleware>();

// Map YARP reverse proxy
app.MapReverseProxy();

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// Prometheus metrics endpoint
app.MapMetrics();

// Startup logging
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Acetone V2 Proxy starting on ports 5000 (HTTP) and 5001 (HTTPS)");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

app.Run();

// Make Program class accessible to tests
public partial class Program { }
