using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Sockets;

namespace Acetone.V2.Core.Resilience;

/// <summary>
/// Implements exponential backoff retry policy for transient failures.
/// </summary>
public class RetryPolicy
{
    private readonly ILogger<RetryPolicy> _logger;
    private readonly ResilienceOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly RetryMetrics _metrics = new();
    private readonly object _metricsLock = new();

    /// <summary>
    /// Initializes a new instance of the RetryPolicy class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Resilience configuration options.</param>
    public RetryPolicy(ILogger<RetryPolicy> logger, ResilienceOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _pipeline = BuildPipeline();
    }

    /// <summary>
    /// Executes an async operation with retry policy.
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

        lock (_metricsLock)
        {
            _metrics.TotalExecutions++;
        }

        try
        {
            var result = await _pipeline.ExecuteAsync(async (ct) =>
            {
                return await operation();
            }, cancellationToken);

            lock (_metricsLock)
            {
                _metrics.SuccessfulExecutions++;
            }

            return result;
        }
        catch
        {
            lock (_metricsLock)
            {
                _metrics.FailedExecutions++;
            }
            throw;
        }
    }

    /// <summary>
    /// Executes an async operation with retry policy and cancellation token support.
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

        lock (_metricsLock)
        {
            _metrics.TotalExecutions++;
        }

        try
        {
            var result = await _pipeline.ExecuteAsync(async (ct) =>
            {
                return await operation(ct);
            }, cancellationToken);

            lock (_metricsLock)
            {
                _metrics.SuccessfulExecutions++;
            }

            return result;
        }
        catch
        {
            lock (_metricsLock)
            {
                _metrics.FailedExecutions++;
            }
            throw;
        }
    }

    /// <summary>
    /// Gets current retry metrics.
    /// </summary>
    /// <returns>Current metrics.</returns>
    public RetryMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            return new RetryMetrics
            {
                TotalRetries = _metrics.TotalRetries,
                TotalExecutions = _metrics.TotalExecutions,
                SuccessfulExecutions = _metrics.SuccessfulExecutions,
                FailedExecutions = _metrics.FailedExecutions
            };
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
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(_options.InitialRetryDelayMs),
                MaxDelay = TimeSpan.FromMilliseconds(_options.MaxRetryDelayMs),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<SocketException>()
                    .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested),
                OnRetry = args =>
                {
                    lock (_metricsLock)
                    {
                        _metrics.TotalRetries++;
                    }

                    _logger.LogWarning(
                        "Retry attempt {AttemptNumber} after {Delay}ms due to {Exception}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.GetType().Name ?? "unknown");

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
