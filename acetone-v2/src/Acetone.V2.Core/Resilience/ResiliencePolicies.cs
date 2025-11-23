using System.Fabric;
using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Acetone.V2.Core.Resilience;

public class ResiliencePolicies : IResiliencePolicies
{
    private readonly AcetoneOptions _options;
    private readonly ILogger<ResiliencePolicies> _logger;
    private readonly AsyncPolicy _policy;

    public ResiliencePolicies(IOptions<AcetoneOptions> options, ILogger<ResiliencePolicies> logger)
    {
        _options = options.Value;
        _logger = logger;
        _policy = CreatePolicy();
    }

    public IAsyncPolicy GetServiceFabricPolicy() => _policy;

    private AsyncPolicy CreatePolicy()
    {
        var retryPolicy = Policy
            .Handle<FabricTransientException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                _options.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(_options.RetryBackoffPower, retryAttempt)),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(exception, "Transient failure. Retrying in {Delay}s (Attempt {Retry}/{Max})", 
                        timeSpan.TotalSeconds, retryCount, _options.RetryCount);
                });

        var circuitBreakerPolicy = Policy
            .Handle<FabricTransientException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                _options.CircuitBreakerThreshold,
                TimeSpan.FromSeconds(_options.CircuitBreakerDurationSeconds),
                (exception, duration) =>
                {
                    _logger.LogError(exception, "Circuit breaker tripped! Breaking for {Duration}s", duration.TotalSeconds);
                },
                () =>
                {
                    _logger.LogInformation("Circuit breaker reset. Resuming normal operation.");
                });

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}
