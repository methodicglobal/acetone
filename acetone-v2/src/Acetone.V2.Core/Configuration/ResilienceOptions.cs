using System.ComponentModel.DataAnnotations;

namespace Acetone.V2.Core.Configuration;

/// <summary>
/// Configuration options for resilience policies.
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// Maximum number of retry attempts for transient failures.
    /// </summary>
    [Range(1, 20)]
    public int RetryCount { get; set; } = 10;

    /// <summary>
    /// Initial delay in milliseconds for exponential backoff retry.
    /// </summary>
    [Range(10, 10000)]
    public int InitialRetryDelayMs { get; set; } = 100;

    /// <summary>
    /// Maximum delay in milliseconds between retry attempts.
    /// </summary>
    [Range(100, 60000)]
    public int MaxRetryDelayMs { get; set; } = 2000;

    /// <summary>
    /// Timeout in milliseconds for each individual attempt.
    /// </summary>
    [Range(100, 300000)]
    public int PerAttemptTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Number of consecutive failures before opening the circuit breaker.
    /// </summary>
    [Range(1, 100)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in milliseconds to keep the circuit breaker open.
    /// </summary>
    [Range(1000, 300000)]
    public int CircuitBreakerBreakDurationMs { get; set; } = 30000;

    /// <summary>
    /// Duration in milliseconds over which failure threshold is calculated.
    /// </summary>
    [Range(1000, 600000)]
    public int CircuitBreakerSamplingDurationMs { get; set; } = 60000;
}
