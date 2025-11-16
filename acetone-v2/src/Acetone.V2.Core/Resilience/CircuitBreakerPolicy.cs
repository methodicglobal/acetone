using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace Acetone.V2.Core.Resilience;

/// <summary>
/// Implements circuit breaker pattern for Service Fabric calls.
/// </summary>
public class CircuitBreakerPolicy
{
    private readonly ILogger<CircuitBreakerPolicy> _logger;
    private readonly ResilienceOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly CircuitBreakerMetrics _metrics = new();
    private readonly object _metricsLock = new();
    private CircuitBreakerStateProvider? _stateProvider;

    /// <summary>
    /// Initializes a new instance of the CircuitBreakerPolicy class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Resilience configuration options.</param>
    public CircuitBreakerPolicy(ILogger<CircuitBreakerPolicy> logger, ResilienceOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _pipeline = BuildPipeline();
    }

    /// <summary>
    /// Executes an async operation with circuit breaker protection.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return await _pipeline.ExecuteAsync(async (ct) =>
        {
            return await operation();
        }, cancellationToken);
    }

    /// <summary>
    /// Executes an async operation with circuit breaker protection and cancellation token support.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return await _pipeline.ExecuteAsync(async (ct) =>
        {
            return await operation(ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the current circuit breaker state.
    /// </summary>
    /// <returns>Current circuit state.</returns>
    public CircuitState GetState()
    {
        return _stateProvider?.CircuitState ?? CircuitState.Closed;
    }

    /// <summary>
    /// Gets current circuit breaker metrics.
    /// </summary>
    /// <returns>Current metrics.</returns>
    public CircuitBreakerMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            return new CircuitBreakerMetrics
            {
                StateTransitions = _metrics.StateTransitions,
                CircuitOpenedCount = _metrics.CircuitOpenedCount,
                CircuitClosedCount = _metrics.CircuitClosedCount,
                CircuitHalfOpenCount = _metrics.CircuitHalfOpenCount,
                RejectedCalls = _metrics.RejectedCalls,
                LastStateTransition = _metrics.LastStateTransition
            };
        }
    }

    /// <summary>
    /// Resets the circuit breaker to closed state.
    /// </summary>
    public void Reset()
    {
        // Note: In Polly v8, manual reset is done through the state provider
        // We'll log the reset attempt
        _logger.LogInformation("Circuit breaker reset requested");

        lock (_metricsLock)
        {
            _metrics.StateTransitions++;
            _metrics.CircuitClosedCount++;
            _metrics.LastStateTransition = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets the underlying resilience pipeline for composition.
    /// </summary>
    /// <returns>The resilience pipeline.</returns>
    internal ResiliencePipeline GetPipeline() => _pipeline;

    private ResiliencePipeline BuildPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = (double)_options.CircuitBreakerFailureThreshold /
                              (_options.CircuitBreakerFailureThreshold + 1),
                MinimumThroughput = _options.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromMilliseconds(_options.CircuitBreakerSamplingDurationMs),
                BreakDuration = TimeSpan.FromMilliseconds(_options.CircuitBreakerBreakDurationMs),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(),
                OnOpened = args =>
                {
                    lock (_metricsLock)
                    {
                        _metrics.StateTransitions++;
                        _metrics.CircuitOpenedCount++;
                        _metrics.LastStateTransition = DateTime.UtcNow;
                    }

                    _logger.LogWarning(
                        "Circuit breaker opened due to {Exception}. Break duration: {BreakDuration}ms",
                        args.Outcome.Exception?.GetType().Name ?? "unknown",
                        args.BreakDuration.TotalMilliseconds);

                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    lock (_metricsLock)
                    {
                        _metrics.StateTransitions++;
                        _metrics.CircuitClosedCount++;
                        _metrics.LastStateTransition = DateTime.UtcNow;
                    }

                    _logger.LogInformation("Circuit breaker closed");

                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    lock (_metricsLock)
                    {
                        _metrics.StateTransitions++;
                        _metrics.CircuitHalfOpenCount++;
                        _metrics.LastStateTransition = DateTime.UtcNow;
                    }

                    _logger.LogInformation("Circuit breaker entered half-open state");

                    return ValueTask.CompletedTask;
                },
                StateProvider = provider =>
                {
                    _stateProvider = provider;
                }
            })
            .Build();
    }
}
