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
            }
        }

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
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
