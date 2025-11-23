using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acetone.V2.TrayApp.Windows.Configuration;

public class ConfigurationManager
{
    private readonly string _configPath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigurationManager(string configPath)
    {
        _configPath = configPath;
    }

    public AcetoneConfiguration Load()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {_configPath}");
        }

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<AcetoneConfiguration>(json, _jsonOptions)
               ?? new AcetoneConfiguration();
    }

    public void Save(AcetoneConfiguration configuration)
    {
        var errors = Validate(configuration);
        if (errors.Count > 0)
        {
            throw new ValidationException($"Configuration validation failed:\n{string.Join("\n", errors)}");
        }

        var json = JsonSerializer.Serialize(configuration, _jsonOptions);

        // Backup existing configuration
        if (File.Exists(_configPath))
        {
            var backupPath = $"{_configPath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_configPath, backupPath, true);
        }

        File.WriteAllText(_configPath, json);
    }

    public List<string> Validate(AcetoneConfiguration configuration)
    {
        var errors = new List<string>();

        // Validate Urls
        if (string.IsNullOrWhiteSpace(configuration.Urls))
        {
            errors.Add("Urls cannot be empty");
        }
        else
        {
            var urls = configuration.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in urls)
            {
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                {
                    errors.Add($"Invalid URL: {url}");
                }
                else if (uri.Scheme != "http" && uri.Scheme != "https")
                {
                    errors.Add($"URL must use http or https scheme: {url}");
                }
                // FINTECH SECURITY: Enforce HTTPS in production (warn for HTTP)
                else if (uri.Scheme == "http" && !IsLocalhost(uri))
                {
                    errors.Add($"SECURITY WARNING: HTTP is not secure for production. Use HTTPS: {url}");
                }
            }
        }

        // Add fintech-specific validation
        ValidateFintechSecurityRequirements(configuration, errors);

        // Validate Service Fabric configuration
        if (configuration.ServiceFabric != null)
        {
            var sf = configuration.ServiceFabric;

            if (string.IsNullOrWhiteSpace(sf.ConnectionEndpoint))
            {
                errors.Add("Service Fabric: Connection endpoint cannot be empty");
            }

            if (!string.IsNullOrWhiteSpace(sf.ServerCertThumbprint))
            {
                if (!IsValidThumbprint(sf.ServerCertThumbprint))
                {
                    errors.Add("Service Fabric: Invalid server certificate thumbprint format");
                }
            }

            if (!string.IsNullOrWhiteSpace(sf.ClientCertThumbprint))
            {
                if (!IsValidThumbprint(sf.ClientCertThumbprint))
                {
                    errors.Add("Service Fabric: Invalid client certificate thumbprint format");
                }
            }

            var validStoreLocations = new[] { "CurrentUser", "LocalMachine" };
            if (!validStoreLocations.Contains(sf.StoreLocation))
            {
                errors.Add($"Service Fabric: Invalid store location. Must be one of: {string.Join(", ", validStoreLocations)}");
            }

            var validProtectionLevels = new[] { "None", "Sign", "EncryptAndSign" };
            if (!validProtectionLevels.Contains(sf.ProtectionLevel))
            {
                errors.Add($"Service Fabric: Invalid protection level. Must be one of: {string.Join(", ", validProtectionLevels)}");
            }
        }

        // Validate Reverse Proxy configuration
        if (configuration.ReverseProxy != null)
        {
            var proxy = configuration.ReverseProxy;

            // Validate routes
            foreach (var (routeId, route) in proxy.Routes)
            {
                if (string.IsNullOrWhiteSpace(route.ClusterId))
                {
                    errors.Add($"Route '{routeId}': ClusterId cannot be empty");
                }
                else if (!proxy.Clusters.ContainsKey(route.ClusterId))
                {
                    errors.Add($"Route '{routeId}': References non-existent cluster '{route.ClusterId}'");
                }

                if (route.Match?.Path == null && (route.Match?.Hosts == null || route.Match.Hosts.Count == 0))
                {
                    errors.Add($"Route '{routeId}': Must specify at least Path or Hosts in Match");
                }

                if (route.TimeoutPolicy.HasValue && route.TimeoutPolicy.Value <= 0)
                {
                    errors.Add($"Route '{routeId}': Timeout must be positive");
                }
            }

            // Validate clusters
            foreach (var (clusterId, cluster) in proxy.Clusters)
            {
                if (cluster.Destinations.Count == 0)
                {
                    errors.Add($"Cluster '{clusterId}': Must have at least one destination");
                }

                foreach (var (destId, dest) in cluster.Destinations)
                {
                    if (string.IsNullOrWhiteSpace(dest.Address))
                    {
                        errors.Add($"Cluster '{clusterId}', Destination '{destId}': Address cannot be empty");
                    }
                    else if (!Uri.TryCreate(dest.Address, UriKind.Absolute, out var uri))
                    {
                        errors.Add($"Cluster '{clusterId}', Destination '{destId}': Invalid address URL");
                    }
                    else if (uri.Scheme != "http" && uri.Scheme != "https")
                    {
                        errors.Add($"Cluster '{clusterId}', Destination '{destId}': Address must use http or https");
                    }
                }

                var validLbPolicies = new[] { "RoundRobin", "LeastRequests", "Random", "PowerOfTwoChoices", "FirstAlphabetical" };
                if (!string.IsNullOrWhiteSpace(cluster.LoadBalancingPolicy) &&
                    !validLbPolicies.Contains(cluster.LoadBalancingPolicy))
                {
                    errors.Add($"Cluster '{clusterId}': Invalid load balancing policy. Must be one of: {string.Join(", ", validLbPolicies)}");
                }

                // Validate health check configuration
                if (cluster.HealthCheck?.Active != null)
                {
                    var active = cluster.HealthCheck.Active;
                    if (active.Enabled)
                    {
                        if (!string.IsNullOrWhiteSpace(active.Interval) && !IsValidTimeSpan(active.Interval))
                        {
                            errors.Add($"Cluster '{clusterId}': Invalid active health check interval format");
                        }

                        if (!string.IsNullOrWhiteSpace(active.Timeout) && !IsValidTimeSpan(active.Timeout))
                        {
                            errors.Add($"Cluster '{clusterId}': Invalid active health check timeout format");
                        }
                    }
                }

                if (cluster.HealthCheck?.Passive != null)
                {
                    var passive = cluster.HealthCheck.Passive;
                    if (passive.Enabled)
                    {
                        if (!string.IsNullOrWhiteSpace(passive.ReactivationPeriod) && !IsValidTimeSpan(passive.ReactivationPeriod))
                        {
                            errors.Add($"Cluster '{clusterId}': Invalid passive health check reactivation period format");
                        }
                    }
                }
            }
        }

        // Validate Rate Limiting configuration
        if (configuration.RateLimiting?.GlobalLimiter != null)
        {
            var limiter = configuration.RateLimiting.GlobalLimiter;
            if (limiter.PermitLimit <= 0)
            {
                errors.Add("Rate Limiting: Permit limit must be positive");
            }

            if (!IsValidTimeSpan(limiter.Window))
            {
                errors.Add("Rate Limiting: Invalid time window format");
            }

            if (limiter.QueueLimit < 0)
            {
                errors.Add("Rate Limiting: Queue limit cannot be negative");
            }
        }

        // Validate Logging configuration
        if (configuration.Logging?.LogLevel != null)
        {
            var validLevels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" };
            foreach (var (category, level) in configuration.Logging.LogLevel)
            {
                if (!validLevels.Contains(level))
                {
                    errors.Add($"Logging: Invalid log level '{level}' for category '{category}'. Must be one of: {string.Join(", ", validLevels)}");
                }
            }
        }

        return errors;
    }

    private static bool IsValidThumbprint(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            return false;

        // Remove spaces and check if it's a valid hex string (40 or 64 characters for SHA-1 or SHA-256)
        var cleaned = thumbprint.Replace(" ", "").Replace(":", "");
        return (cleaned.Length == 40 || cleaned.Length == 64) &&
               cleaned.All(c => char.IsDigit(c) || (char.ToUpper(c) >= 'A' && char.ToUpper(c) <= 'F'));
    }

    private static bool IsValidTimeSpan(string timeSpan)
    {
        return TimeSpan.TryParse(timeSpan, out _);
    }

    private static bool IsLocalhost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host == "localhost" ||
               host == "127.0.0.1" ||
               host == "::1" ||
               host == "0.0.0.0" ||
               host.StartsWith("192.168.") ||
               host.StartsWith("10.") ||
               (host.StartsWith("172.") && host.Split('.').Length >= 2 &&
                int.TryParse(host.Split('.')[1], out var second) && second >= 16 && second <= 31);
    }

    /// <summary>
    /// Validates fintech-specific security requirements for production deployments.
    /// </summary>
    private void ValidateFintechSecurityRequirements(AcetoneConfiguration configuration, List<string> errors)
    {
        // 1. Enforce HTTPS for backend destinations
        if (configuration.ReverseProxy != null)
        {
            foreach (var (clusterId, cluster) in configuration.ReverseProxy.Clusters)
            {
                foreach (var (destId, dest) in cluster.Destinations)
                {
                    if (Uri.TryCreate(dest.Address, UriKind.Absolute, out var uri))
                    {
                        if (uri.Scheme == "http" && !IsLocalhost(uri))
                        {
                            errors.Add($"SECURITY: Cluster '{clusterId}', Destination '{destId}' uses insecure HTTP. HTTPS required for production.");
                        }
                    }
                }
            }
        }

        // 2. Enforce strong Service Fabric protection level
        if (configuration.ServiceFabric != null)
        {
            if (configuration.ServiceFabric.ProtectionLevel == "None")
            {
                errors.Add("SECURITY: Service Fabric protection level 'None' is not allowed in fintech environments. Use 'EncryptAndSign'.");
            }
            else if (configuration.ServiceFabric.ProtectionLevel == "Sign")
            {
                errors.Add("SECURITY WARNING: Service Fabric protection level 'Sign' provides integrity but not confidentiality. Consider 'EncryptAndSign' for sensitive data.");
            }

            // 3. Require certificate-based authentication for Service Fabric
            if (string.IsNullOrWhiteSpace(configuration.ServiceFabric.ServerCertThumbprint) &&
                string.IsNullOrWhiteSpace(configuration.ServiceFabric.ClientCertThumbprint))
            {
                errors.Add("SECURITY: Service Fabric requires certificate-based authentication for production. Configure server and client certificates.");
            }

            // 4. Validate certificate thumbprints are not using weak SHA-1 (40 chars)
            if (!string.IsNullOrWhiteSpace(configuration.ServiceFabric.ServerCertThumbprint))
            {
                var cleaned = configuration.ServiceFabric.ServerCertThumbprint.Replace(" ", "").Replace(":", "");
                if (cleaned.Length == 40)
                {
                    errors.Add("SECURITY WARNING: Server certificate appears to use SHA-1 (40 characters). SHA-256 (64 characters) is recommended.");
                }
            }

            if (!string.IsNullOrWhiteSpace(configuration.ServiceFabric.ClientCertThumbprint))
            {
                var cleaned = configuration.ServiceFabric.ClientCertThumbprint.Replace(" ", "").Replace(":", "");
                if (cleaned.Length == 40)
                {
                    errors.Add("SECURITY WARNING: Client certificate appears to use SHA-1 (40 characters). SHA-256 (64 characters) is recommended.");
                }
            }
        }

        // 5. Require rate limiting for production
        if (configuration.RateLimiting == null || configuration.RateLimiting.GlobalLimiter == null)
        {
            errors.Add("SECURITY WARNING: Rate limiting is not configured. This is required for DDoS protection and abuse prevention.");
        }
        else
        {
            // Validate rate limiting is not too permissive
            if (configuration.RateLimiting.GlobalLimiter.PermitLimit > 10000)
            {
                errors.Add("SECURITY WARNING: Rate limit is very high (>10,000 requests). Consider lowering for better protection.");
            }
        }

        // 6. Require health checks for clusters
        if (configuration.ReverseProxy != null)
        {
            foreach (var (clusterId, cluster) in configuration.ReverseProxy.Clusters)
            {
                if (cluster.HealthCheck == null ||
                    (cluster.HealthCheck.Active?.Enabled != true && cluster.HealthCheck.Passive?.Enabled != true))
                {
                    errors.Add($"SECURITY WARNING: Cluster '{clusterId}' has no health checks enabled. Active or passive health checks are recommended.");
                }
            }
        }

        // 7. Validate request timeouts are set (prevent slowloris attacks)
        if (configuration.ReverseProxy != null)
        {
            foreach (var (routeId, route) in configuration.ReverseProxy.Routes)
            {
                if (!route.TimeoutPolicy.HasValue || route.TimeoutPolicy.Value > 300)
                {
                    errors.Add($"SECURITY WARNING: Route '{routeId}' has no timeout or timeout >300s. Configure appropriate timeouts to prevent resource exhaustion.");
                }
            }
        }

        // 8. Check for default/insecure logging levels in production
        if (configuration.Logging?.LogLevel != null)
        {
            if (configuration.Logging.LogLevel.TryGetValue("Default", out var defaultLevel))
            {
                if (defaultLevel == "Trace" || defaultLevel == "Debug")
                {
                    errors.Add("SECURITY WARNING: Default log level is 'Trace' or 'Debug'. These verbose levels may log sensitive data and should not be used in production.");
                }
            }
        }

        // 9. Validate destinations have appropriate health check paths
        if (configuration.ReverseProxy != null)
        {
            foreach (var (clusterId, cluster) in configuration.ReverseProxy.Clusters)
            {
                if (cluster.HealthCheck?.Active?.Enabled == true)
                {
                    if (string.IsNullOrWhiteSpace(cluster.HealthCheck.Active.Path))
                    {
                        errors.Add($"SECURITY WARNING: Cluster '{clusterId}' has active health checks enabled but no path specified.");
                    }
                }
            }
        }

        // 10. Validate session affinity configuration for security
        if (configuration.ReverseProxy != null)
        {
            foreach (var (clusterId, cluster) in configuration.ReverseProxy.Clusters)
            {
                if (cluster.SessionAffinity?.Enabled == true)
                {
                    if (cluster.SessionAffinity.Policy == "CustomHeader")
                    {
                        errors.Add($"SECURITY WARNING: Cluster '{clusterId}' uses CustomHeader session affinity. Ensure the header cannot be spoofed by clients.");
                    }
                }
            }
        }
    }
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
