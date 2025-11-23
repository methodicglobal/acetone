using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Acetone.V2.TrayApp.Windows.Configuration;

public class AcetoneConfiguration
{
    [JsonPropertyName("Logging")]
    public LoggingConfiguration Logging { get; set; } = new();

    [JsonPropertyName("AllowedHosts")]
    public string AllowedHosts { get; set; } = "*";

    [JsonPropertyName("Urls")]
    public string Urls { get; set; } = "http://0.0.0.0:8080";

    [JsonPropertyName("ServiceFabric")]
    public ServiceFabricConfiguration? ServiceFabric { get; set; }

    [JsonPropertyName("ReverseProxy")]
    public ReverseProxyConfiguration? ReverseProxy { get; set; }

    [JsonPropertyName("RateLimiting")]
    public RateLimitingConfiguration? RateLimiting { get; set; }

    [JsonPropertyName("HealthChecks")]
    public HealthChecksConfiguration? HealthChecks { get; set; }
}

public class LoggingConfiguration
{
    [JsonPropertyName("LogLevel")]
    public Dictionary<string, string> LogLevel { get; set; } = new()
    {
        ["Default"] = "Information",
        ["Microsoft.AspNetCore"] = "Warning",
        ["Yarp"] = "Information"
    };
}

public class ServiceFabricConfiguration
{
    [JsonPropertyName("ConnectionEndpoint")]
    [DisplayName("Connection Endpoint")]
    [Description("Service Fabric cluster connection endpoint (e.g., localhost:19000)")]
    public string ConnectionEndpoint { get; set; } = "localhost:19000";

    [JsonPropertyName("ServerCertThumbprint")]
    [DisplayName("Server Certificate Thumbprint")]
    [Description("Thumbprint of the Service Fabric cluster certificate")]
    public string? ServerCertThumbprint { get; set; }

    [JsonPropertyName("ClientCertThumbprint")]
    [DisplayName("Client Certificate Thumbprint")]
    [Description("Thumbprint of the client certificate for authentication")]
    public string? ClientCertThumbprint { get; set; }

    [JsonPropertyName("StoreLocation")]
    [DisplayName("Certificate Store Location")]
    [Description("Certificate store location (CurrentUser or LocalMachine)")]
    public string StoreLocation { get; set; } = "CurrentUser";

    [JsonPropertyName("StoreName")]
    [DisplayName("Certificate Store Name")]
    [Description("Certificate store name (My, Root, etc.)")]
    public string StoreName { get; set; } = "My";

    [JsonPropertyName("FindType")]
    [DisplayName("Certificate Find Type")]
    [Description("How to find the certificate (FindByThumbprint, FindBySubjectName, etc.)")]
    public string FindType { get; set; } = "FindByThumbprint";

    [JsonPropertyName("ServerCommonNames")]
    [DisplayName("Server Common Names")]
    [Description("List of valid server certificate common names (comma-separated)")]
    public string? ServerCommonNames { get; set; }

    [JsonPropertyName("ProtectionLevel")]
    [DisplayName("Protection Level")]
    [Description("Security protection level (None, Sign, EncryptAndSign)")]
    public string ProtectionLevel { get; set; } = "EncryptAndSign";
}

public class ReverseProxyConfiguration
{
    [JsonPropertyName("Routes")]
    public Dictionary<string, RouteConfiguration> Routes { get; set; } = new();

    [JsonPropertyName("Clusters")]
    public Dictionary<string, ClusterConfiguration> Clusters { get; set; } = new();
}

public class RouteConfiguration
{
    [JsonPropertyName("ClusterId")]
    [DisplayName("Cluster ID")]
    [Description("The cluster this route forwards to")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("Match")]
    public RouteMatchConfiguration Match { get; set; } = new();

    [JsonPropertyName("Transforms")]
    public List<Dictionary<string, string>>? Transforms { get; set; }

    [JsonPropertyName("AuthorizationPolicy")]
    [DisplayName("Authorization Policy")]
    [Description("Name of the authorization policy to apply")]
    public string? AuthorizationPolicy { get; set; }

    [JsonPropertyName("CorsPolicy")]
    [DisplayName("CORS Policy")]
    [Description("Name of the CORS policy to apply")]
    public string? CorsPolicy { get; set; }

    [JsonPropertyName("TimeoutPolicy")]
    [DisplayName("Timeout Policy")]
    [Description("Request timeout in seconds")]
    public int? TimeoutPolicy { get; set; }
}

public class RouteMatchConfiguration
{
    [JsonPropertyName("Path")]
    [DisplayName("Path Pattern")]
    [Description("URL path pattern to match (e.g., /api/{**catch-all})")]
    public string? Path { get; set; }

    [JsonPropertyName("Hosts")]
    [DisplayName("Host Names")]
    [Description("Host names to match (comma-separated)")]
    public List<string>? Hosts { get; set; }

    [JsonPropertyName("Methods")]
    [DisplayName("HTTP Methods")]
    [Description("HTTP methods to match (comma-separated)")]
    public List<string>? Methods { get; set; }

    [JsonPropertyName("Headers")]
    public List<HeaderMatchConfiguration>? Headers { get; set; }
}

public class HeaderMatchConfiguration
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Values")]
    public List<string>? Values { get; set; }

    [JsonPropertyName("Mode")]
    public string? Mode { get; set; }
}

public class ClusterConfiguration
{
    [JsonPropertyName("Destinations")]
    public Dictionary<string, DestinationConfiguration> Destinations { get; set; } = new();

    [JsonPropertyName("LoadBalancingPolicy")]
    [DisplayName("Load Balancing Policy")]
    [Description("Load balancing algorithm (RoundRobin, LeastRequests, Random, PowerOfTwoChoices)")]
    public string? LoadBalancingPolicy { get; set; } = "RoundRobin";

    [JsonPropertyName("SessionAffinity")]
    public SessionAffinityConfiguration? SessionAffinity { get; set; }

    [JsonPropertyName("HealthCheck")]
    public HealthCheckConfiguration? HealthCheck { get; set; }

    [JsonPropertyName("HttpRequest")]
    public HttpRequestConfiguration? HttpRequest { get; set; }

    [JsonPropertyName("Metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public class DestinationConfiguration
{
    [JsonPropertyName("Address")]
    [DisplayName("Destination Address")]
    [Description("Backend server URL (e.g., https://backend.example.com)")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("Health")]
    [DisplayName("Health Check URL")]
    [Description("Override health check URL for this destination")]
    public string? Health { get; set; }

    [JsonPropertyName("Metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public class SessionAffinityConfiguration
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("Policy")]
    [DisplayName("Affinity Policy")]
    [Description("Session affinity policy (Cookie, CustomHeader)")]
    public string? Policy { get; set; }

    [JsonPropertyName("FailurePolicy")]
    [DisplayName("Failure Policy")]
    [Description("What to do when affinity fails (Redistribute, Return503)")]
    public string? FailurePolicy { get; set; }

    [JsonPropertyName("AffinityKeyName")]
    [DisplayName("Affinity Key Name")]
    [Description("Cookie or header name for affinity")]
    public string? AffinityKeyName { get; set; }
}

public class HealthCheckConfiguration
{
    [JsonPropertyName("Active")]
    public ActiveHealthCheckConfiguration? Active { get; set; }

    [JsonPropertyName("Passive")]
    public PassiveHealthCheckConfiguration? Passive { get; set; }

    [JsonPropertyName("AvailableDestinationsPolicy")]
    [DisplayName("Available Destinations Policy")]
    [Description("Policy when destinations are unavailable (HealthyOrPanic, HealthyAndUnknown)")]
    public string? AvailableDestinationsPolicy { get; set; }
}

public class ActiveHealthCheckConfiguration
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("Interval")]
    [DisplayName("Check Interval")]
    [Description("Time between health checks (e.g., 00:00:10 for 10 seconds)")]
    public string? Interval { get; set; } = "00:00:10";

    [JsonPropertyName("Timeout")]
    [DisplayName("Check Timeout")]
    [Description("Health check request timeout (e.g., 00:00:05 for 5 seconds)")]
    public string? Timeout { get; set; } = "00:00:05";

    [JsonPropertyName("Policy")]
    [DisplayName("Health Policy")]
    [Description("Health check policy (ConsecutiveFailures, etc.)")]
    public string? Policy { get; set; } = "ConsecutiveFailures";

    [JsonPropertyName("Path")]
    [DisplayName("Health Check Path")]
    [Description("Health check endpoint path (e.g., /health)")]
    public string? Path { get; set; } = "/health";
}

public class PassiveHealthCheckConfiguration
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("Policy")]
    [DisplayName("Passive Policy")]
    [Description("Passive health check policy (TransportFailureRate, etc.)")]
    public string? Policy { get; set; } = "TransportFailureRate";

    [JsonPropertyName("ReactivationPeriod")]
    [DisplayName("Reactivation Period")]
    [Description("Time before retrying unhealthy destination (e.g., 00:01:00)")]
    public string? ReactivationPeriod { get; set; } = "00:01:00";
}

public class HttpRequestConfiguration
{
    [JsonPropertyName("ActivityTimeout")]
    [DisplayName("Activity Timeout")]
    [Description("Maximum time for request activity (e.g., 00:02:00)")]
    public string? ActivityTimeout { get; set; }

    [JsonPropertyName("Version")]
    [DisplayName("HTTP Version")]
    [Description("HTTP version to use (1.1, 2.0, 3.0)")]
    public string? Version { get; set; }

    [JsonPropertyName("VersionPolicy")]
    [DisplayName("Version Policy")]
    [Description("HTTP version selection policy (RequestVersionOrLower, RequestVersionOrHigher, RequestVersionExact)")]
    public string? VersionPolicy { get; set; }

    [JsonPropertyName("AllowResponseBuffering")]
    [DisplayName("Allow Response Buffering")]
    public bool? AllowResponseBuffering { get; set; }
}

public class RateLimitingConfiguration
{
    [JsonPropertyName("GlobalLimiter")]
    public GlobalRateLimiterConfiguration? GlobalLimiter { get; set; }

    [JsonPropertyName("Policies")]
    public Dictionary<string, RateLimitPolicyConfiguration>? Policies { get; set; }
}

public class GlobalRateLimiterConfiguration
{
    [JsonPropertyName("PermitLimit")]
    [DisplayName("Permit Limit")]
    [Description("Maximum number of requests allowed")]
    public int PermitLimit { get; set; } = 100;

    [JsonPropertyName("Window")]
    [DisplayName("Time Window")]
    [Description("Time window for rate limiting (e.g., 00:01:00 for 1 minute)")]
    public string Window { get; set; } = "00:01:00";

    [JsonPropertyName("QueueLimit")]
    [DisplayName("Queue Limit")]
    [Description("Maximum number of queued requests")]
    public int QueueLimit { get; set; } = 0;
}

public class RateLimitPolicyConfiguration
{
    [JsonPropertyName("PermitLimit")]
    public int PermitLimit { get; set; }

    [JsonPropertyName("Window")]
    public string Window { get; set; } = "00:01:00";
}

public class HealthChecksConfiguration
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("Path")]
    [DisplayName("Health Check Path")]
    [Description("Endpoint path for health checks")]
    public string Path { get; set; } = "/health";
}
