using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Acetone.V2.Proxy.Yarp;

/// <summary>
/// Custom IProxyConfigProvider that creates dynamic YARP configuration for Service Fabric routing
/// </summary>
public class ServiceFabricProxyConfigProvider : IProxyConfigProvider
{
    private readonly ILogger<ServiceFabricProxyConfigProvider> _logger;
    private volatile ServiceFabricProxyConfig? _config;
    private CancellationTokenSource _changeTokenSource = new();

    public ServiceFabricProxyConfigProvider(ILogger<ServiceFabricProxyConfigProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the current proxy configuration
    /// </summary>
    public IProxyConfig GetConfig()
    {
        if (_config == null)
        {
            _logger.LogDebug("Creating new YARP configuration for Service Fabric");
            _config = new ServiceFabricProxyConfig(this);
        }

        return _config;
    }

    /// <summary>
    /// Gets a change token that will be triggered when the configuration is updated
    /// </summary>
    public IChangeToken GetChangeToken()
    {
        return new CancellationChangeToken(_changeTokenSource.Token);
    }

    /// <summary>
    /// Triggers a configuration update, notifying all listeners
    /// </summary>
    public void Update()
    {
        _logger.LogInformation("Triggering YARP configuration update");

        // Invalidate the cached config
        _config = null;

        // Signal change to all listeners
        var oldTokenSource = _changeTokenSource;
        _changeTokenSource = new CancellationTokenSource();
        oldTokenSource.Cancel();
        oldTokenSource.Dispose();
    }

    /// <summary>
    /// Internal implementation of IProxyConfig
    /// </summary>
    private class ServiceFabricProxyConfig : IProxyConfig
    {
        private readonly IReadOnlyList<RouteConfig> _routes;
        private readonly IReadOnlyList<ClusterConfig> _clusters;
        private readonly IChangeToken _changeToken;

        public ServiceFabricProxyConfig(ServiceFabricProxyConfigProvider provider)
        {
            // Build the catch-all route
            _routes = new List<RouteConfig>
            {
                DynamicRouteBuilder.BuildCatchAllRoute()
            };

            // Build the default cluster
            _clusters = new List<ClusterConfig>
            {
                DynamicRouteBuilder.BuildDefaultCluster()
            };

            _changeToken = provider.GetChangeToken();
        }

        public IReadOnlyList<RouteConfig> Routes => _routes;
        public IReadOnlyList<ClusterConfig> Clusters => _clusters;
        public IChangeToken ChangeToken => _changeToken;
    }
}
