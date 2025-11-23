using System.ComponentModel.DataAnnotations;

namespace Acetone.V2.Core.Configuration;

public class AcetoneOptions
{
    public const string SectionName = "Acetone";

    [Required]
    public string[] ClusterConnectionStrings { get; set; } = Array.Empty<string>();

    public ApplicationNameLocation ApplicationNameLocation { get; set; } = ApplicationNameLocation.Subdomain;

    public CredentialsType CredentialsType { get; set; } = CredentialsType.Local;

    public string? ClientCertificateThumbprint { get; set; }

    public string[]? ServerCertificateThumbprints { get; set; }

    public string? ClientCertificateSubjectDistinguishedName { get; set; }

    public string? ClientCertificateIssuerDistinguishedName { get; set; }

    public string[]? ServerCertificateCommonNames { get; set; }

    [Range(1, 1000)]
    public int PartitionCacheLimit { get; set; } = 5;

    [Range(1, 3600)]
    public int PartitionCacheTtlSeconds { get; set; } = 30;

    [Range(1, 100)]
    public int PartitionResolveMaxAttempts { get; set; } = 10;

    [Range(1, 10000)]
    public int PartitionResolveInitialDelayMs { get; set; } = 100;

    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public bool EnableLogging { get; set; } = true;

    public bool DisablePartitionCache { get; set; } = false;

    // Resilience Options
    public int RetryCount { get; set; } = 3;
    public int RetryBackoffPower { get; set; } = 2; // Power for exponential backoff
    public int CircuitBreakerThreshold { get; set; } = 5;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}
