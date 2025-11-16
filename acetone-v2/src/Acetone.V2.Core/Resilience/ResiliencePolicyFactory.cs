using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace Acetone.V2.Core.Resilience;

/// <summary>
/// Factory for creating configured resilience policies.
/// </summary>
public class ResiliencePolicyFactory
{
    private readonly ILogger<ResiliencePolicyFactory> _logger;
    private readonly AcetoneOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private RetryPolicy? _retryPolicy;
    private CircuitBreakerPolicy? _circuitBreakerPolicy;
    private ResiliencePipeline? _timeoutPipeline;
    private ResiliencePipeline? _combinedPipeline;
    private ResiliencePipeline? _fullPipeline;

    /// <summary>
    /// Initializes a new instance of the ResiliencePolicyFactory class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Acetone configuration options.</param>
    public ResiliencePolicyFactory(
        ILogger<ResiliencePolicyFactory> logger,
        IOptions<AcetoneOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Create a logger factory for creating loggers for individual policies
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            // This is a simple factory - in production, this would use the DI container's logger factory
        });
    }

    /// <summary>
    /// Initializes a new instance of the ResiliencePolicyFactory class with a logger factory.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Acetone configuration options.</param>
    /// <param name="loggerFactory">Logger factory for creating policy loggers.</param>
    public ResiliencePolicyFactory(
        ILogger<ResiliencePolicyFactory> logger,
        IOptions<AcetoneOptions> options,
        ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Creates a retry policy with exponential backoff.
    /// </summary>
    /// <returns>Configured retry policy.</returns>
    public RetryPolicy CreateRetryPolicy()
    {
        var retryLogger = _loggerFactory.CreateLogger<RetryPolicy>();
        _retryPolicy = new RetryPolicy(retryLogger, _options.Resilience);

        _logger.LogDebug(
            "Created retry policy: MaxRetries={MaxRetries}, InitialDelay={InitialDelay}ms, MaxDelay={MaxDelay}ms",
            _options.Resilience.RetryCount,
            _options.Resilience.InitialRetryDelayMs,
            _options.Resilience.MaxRetryDelayMs);

        return _retryPolicy;
    }

    /// <summary>
    /// Creates a circuit breaker policy.
    /// </summary>
    /// <returns>Configured circuit breaker policy.</returns>
    public CircuitBreakerPolicy CreateCircuitBreakerPolicy()
    {
        var cbLogger = _loggerFactory.CreateLogger<CircuitBreakerPolicy>();
        _circuitBreakerPolicy = new CircuitBreakerPolicy(cbLogger, _options.Resilience);

        _logger.LogDebug(
            "Created circuit breaker policy: FailureThreshold={FailureThreshold}, BreakDuration={BreakDuration}ms",
            _options.Resilience.CircuitBreakerFailureThreshold,
            _options.Resilience.CircuitBreakerBreakDurationMs);

        return _circuitBreakerPolicy;
    }

    /// <summary>
    /// Creates a timeout policy for per-attempt timeouts.
    /// </summary>
    /// <returns>Configured timeout policy.</returns>
    public ResiliencePipeline CreateTimeoutPolicy()
    {
        _timeoutPipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromMilliseconds(_options.Resilience.PerAttemptTimeoutMs),
                OnTimeout = args =>
                {
                    _logger.LogWarning(
                        "Operation timed out after {Timeout}ms",
                        args.Timeout.TotalMilliseconds);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        _logger.LogDebug(
            "Created timeout policy: Timeout={Timeout}ms",
            _options.Resilience.PerAttemptTimeoutMs);

        return _timeoutPipeline;
    }

    /// <summary>
    /// Creates a combined policy with retry and circuit breaker.
    /// </summary>
    /// <returns>Combined resilience pipeline.</returns>
    public ResiliencePipeline CreateCombinedPolicy()
    {
        var retryPolicy = CreateRetryPolicy();
        var circuitBreakerPolicy = CreateCircuitBreakerPolicy();

        // Wrap operations: outer retry -> inner circuit breaker
        _combinedPipeline = new ResiliencePipelineBuilder()
            .AddPipeline(retryPolicy.GetPipeline())
            .AddPipeline(circuitBreakerPolicy.GetPipeline())
            .Build();

        _logger.LogDebug("Created combined resilience policy (retry + circuit breaker)");

        return _combinedPipeline;
    }

    /// <summary>
    /// Creates a full resilience pipeline with all policies (timeout, retry, circuit breaker).
    /// </summary>
    /// <returns>Full resilience pipeline.</returns>
    public ResiliencePipeline CreateFullResiliencePipeline()
    {
        var timeoutPolicy = CreateTimeoutPolicy();
        var retryPolicy = CreateRetryPolicy();
        var circuitBreakerPolicy = CreateCircuitBreakerPolicy();

        // Policy order: timeout (innermost) -> retry -> circuit breaker (outermost)
        // This ensures: each attempt has a timeout, retries are attempted, and circuit breaker wraps everything
        _fullPipeline = new ResiliencePipelineBuilder()
            .AddPipeline(circuitBreakerPolicy.GetPipeline())
            .AddPipeline(retryPolicy.GetPipeline())
            .AddPipeline(timeoutPolicy)
            .Build();

        _logger.LogInformation(
            "Created full resilience pipeline: Timeout={Timeout}ms, Retry={Retry}x, CircuitBreaker={CB}",
            _options.Resilience.PerAttemptTimeoutMs,
            _options.Resilience.RetryCount,
            _options.Resilience.CircuitBreakerFailureThreshold);

        return _fullPipeline;
    }

    /// <summary>
    /// Gets the current retry policy instance, or creates one if it doesn't exist.
    /// </summary>
    /// <returns>Retry policy instance.</returns>
    public RetryPolicy GetRetryPolicy()
    {
        return _retryPolicy ?? CreateRetryPolicy();
    }

    /// <summary>
    /// Gets the current circuit breaker policy instance, or creates one if it doesn't exist.
    /// </summary>
    /// <returns>Circuit breaker policy instance.</returns>
    public CircuitBreakerPolicy GetCircuitBreakerPolicy()
    {
        return _circuitBreakerPolicy ?? CreateCircuitBreakerPolicy();
    }

    /// <summary>
    /// Gets the current timeout policy instance, or creates one if it doesn't exist.
    /// </summary>
    /// <returns>Timeout policy instance.</returns>
    public ResiliencePipeline GetTimeoutPolicy()
    {
        return _timeoutPipeline ?? CreateTimeoutPolicy();
    }

    /// <summary>
    /// Gets the current combined policy instance, or creates one if it doesn't exist.
    /// </summary>
    /// <returns>Combined policy instance.</returns>
    public ResiliencePipeline GetCombinedPolicy()
    {
        return _combinedPipeline ?? CreateCombinedPolicy();
    }

    /// <summary>
    /// Gets the current full resilience pipeline instance, or creates one if it doesn't exist.
    /// </summary>
    /// <returns>Full resilience pipeline instance.</returns>
    public ResiliencePipeline GetFullResiliencePipeline()
    {
        return _fullPipeline ?? CreateFullResiliencePipeline();
    }
}
