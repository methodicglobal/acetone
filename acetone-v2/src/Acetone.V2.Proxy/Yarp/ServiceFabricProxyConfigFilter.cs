using System.Reflection;
using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Yarp;

/// <summary>
/// Configures YARP request and response transforms for Service Fabric proxy scenarios.
/// </summary>
public class ServiceFabricProxyConfigFilter : IProxyConfigFilter
{
    private static readonly string AcetoneVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
    {
        // No cluster-level configuration needed
        return new ValueTask<ClusterConfig>(cluster);
    }

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        // Build transforms list
        var transforms = new List<IReadOnlyDictionary<string, string>>();

        // Add existing transforms if any
        if (route.Transforms != null)
        {
            transforms.AddRange(route.Transforms);
        }

        // Request transforms
        AddRequestTransforms(transforms);

        // Response transforms
        AddResponseTransforms(transforms);

        // Create updated route with transforms
        var updatedRoute = route with
        {
            Transforms = transforms
        };

        return new ValueTask<RouteConfig>(updatedRoute);
    }

    private void AddRequestTransforms(List<IReadOnlyDictionary<string, string>> transforms)
    {
        // Preserve original host header
        transforms.Add(new Dictionary<string, string>
        {
            { "RequestHeaderOriginalHost", "true" }
        });

        // Add X-Forwarded headers
        transforms.Add(new Dictionary<string, string>
        {
            { "X-Forwarded", "Set" }
        });

        // Add X-Correlation-Id header (if not present)
        transforms.Add(new Dictionary<string, string>
        {
            { "RequestHeader", "X-Correlation-Id" },
            { "Append", System.Guid.NewGuid().ToString() }
        });

        // Add X-Acetone-Version header
        transforms.Add(new Dictionary<string, string>
        {
            { "RequestHeader", "X-Acetone-Version" },
            { "Set", AcetoneVersion }
        });

        // Remove sensitive headers
        RemoveSensitiveHeaders(transforms);
    }

    private void RemoveSensitiveHeaders(List<IReadOnlyDictionary<string, string>> transforms)
    {
        var sensitiveHeaders = new[]
        {
            "X-API-Key",
            "X-Internal-Token",
            "X-Service-Token"
        };

        foreach (var header in sensitiveHeaders)
        {
            transforms.Add(new Dictionary<string, string>
            {
                { "RequestHeaderRemove", header }
            });
        }
    }

    private void AddResponseTransforms(List<IReadOnlyDictionary<string, string>> transforms)
    {
        // Add X-Acetone-Resolution-Time-Ms header (placeholder, actual value set at runtime)
        transforms.Add(new Dictionary<string, string>
        {
            { "ResponseHeader", "X-Acetone-Resolution-Time-Ms" },
            { "Append", "0" }
        });

        // Add X-Acetone-Cache-Hit header (placeholder, actual value set at runtime)
        transforms.Add(new Dictionary<string, string>
        {
            { "ResponseHeader", "X-Acetone-Cache-Hit" },
            { "Append", "false" }
        });
    }
}
