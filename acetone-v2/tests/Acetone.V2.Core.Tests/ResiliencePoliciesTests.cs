using System.Fabric;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Xunit;

namespace Acetone.V2.Core.Tests;

public class ResiliencePoliciesTests
{
    [Fact]
    public async Task GetServiceFabricPolicy_RetriesOnTransientFailure()
    {
        // Arrange
        var options = new AcetoneOptions
        {
            RetryCount = 2,
            RetryBackoffPower = 1 // Linear for test speed
        };
        var logger = NullLogger<ResiliencePolicies>.Instance;
        var policies = new ResiliencePolicies(Options.Create(options), logger);
        var policy = policies.GetServiceFabricPolicy();

        int attempts = 0;

        // Act & Assert
        await Assert.ThrowsAsync<FabricTransientException>(async () =>
        {
            await policy.ExecuteAsync(() =>
            {
                attempts++;
                throw new FabricTransientException("Transient failure");
            });
        });

        // Initial attempt + 2 retries = 3 total attempts
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task GetServiceFabricPolicy_BreaksCircuit_AfterThreshold()
    {
        // Arrange
        var options = new AcetoneOptions
        {
            RetryCount = 0, // Disable retries to test circuit breaker directly
            CircuitBreakerThreshold = 2,
            CircuitBreakerDurationSeconds = 1
        };
        var logger = NullLogger<ResiliencePolicies>.Instance;
        var policies = new ResiliencePolicies(Options.Create(options), logger);
        var policy = policies.GetServiceFabricPolicy();

        // Act
        // Fail 1
        await Assert.ThrowsAsync<FabricTransientException>(() => policy.ExecuteAsync(() => throw new FabricTransientException("Fail 1")));
        
        // Fail 2 (Threshold reached)
        await Assert.ThrowsAsync<FabricTransientException>(() => policy.ExecuteAsync(() => throw new FabricTransientException("Fail 2")));

        // Assert
        // Next call should throw BrokenCircuitException immediately
        await Assert.ThrowsAsync<BrokenCircuitException>(() => policy.ExecuteAsync(() => throw new FabricTransientException("Fail 3")));
    }
}
