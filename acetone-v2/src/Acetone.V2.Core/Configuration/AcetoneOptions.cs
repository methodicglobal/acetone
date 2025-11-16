using System.ComponentModel.DataAnnotations;

namespace Acetone.V2.Core.Configuration;

/// <summary>
/// Main configuration options for Acetone V2.
/// </summary>
public class AcetoneOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Acetone";

    /// <summary>
    /// Resilience policy configuration.
    /// </summary>
    [Required]
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Service Fabric cluster connection string.
    /// </summary>
    public string? ServiceFabricConnectionString { get; set; }

    /// <summary>
    /// Enable detailed logging for troubleshooting.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Maximum concurrent requests to Service Fabric.
    /// </summary>
    [Range(1, 1000)]
    public int MaxConcurrentRequests { get; set; } = 100;
}
