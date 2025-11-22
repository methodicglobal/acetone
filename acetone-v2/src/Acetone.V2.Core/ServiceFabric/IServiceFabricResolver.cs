using System.Fabric;

namespace Acetone.V2.Core.ServiceFabric;

public interface IServiceFabricResolver
{
    /// <summary>
    /// Resolves the URL for a stateless service within an application.
    /// </summary>
    /// <param name="applicationName">The name of the application (e.g., "my-service").</param>
    /// <param name="invocationId">A unique ID for tracing the request.</param>
    /// <param name="version">Optional application version filter.</param>
    /// <param name="refreshCache">Whether to force a cache refresh.</param>
    /// <returns>The resolved endpoint URL.</returns>
    Task<string> ResolveUrlAsync(string applicationName, Guid invocationId, string? version = null, bool refreshCache = false);

    /// <summary>
    /// Resolves the URL for a function service within an application.
    /// </summary>
    /// <param name="applicationName">The name of the application.</param>
    /// <param name="invocationId">A unique ID for tracing the request.</param>
    /// <param name="version">Optional application version filter.</param>
    /// <param name="refreshCache">Whether to force a cache refresh.</param>
    /// <returns>The resolved endpoint URL.</returns>
    Task<string> ResolveFunctionUriAsync(string applicationName, Guid invocationId, string? version = null, bool refreshCache = false);
}
