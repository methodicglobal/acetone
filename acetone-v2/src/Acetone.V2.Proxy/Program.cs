using Acetone.V2.Core;
using Yarp.ReverseProxy.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add Acetone Core Services
builder.Services.AddAcetone(builder.Configuration);

// Add YARP
builder.Services.AddReverseProxy();
    
builder.Services.AddSingleton<IProxyConfigProvider, Acetone.V2.Proxy.Configuration.ServiceFabricProxyConfigProvider>();
builder.Services.AddSingleton<IProxyConfigFilter, Acetone.V2.Proxy.Configuration.ServiceFabricProxyConfigFilter>();
builder.Services.AddHealthChecks()
    .AddCheck<Acetone.V2.Proxy.Health.ServiceFabricHealthCheck>("service_fabric_readiness");

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(Acetone.V2.Core.Diagnostics.AcetoneTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddSource(Acetone.V2.Core.Diagnostics.AcetoneTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapReverseProxy(pipeline =>
{
    pipeline.UseMiddleware<Acetone.V2.Proxy.Middleware.ServiceFabricRoutingMiddleware>();
});

app.MapGet("/", () => "Acetone V2 Proxy is running!");

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Liveness only checks if app is up
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Name == "service_fabric_readiness"
});

app.MapPrometheusScrapingEndpoint();

app.Run();
