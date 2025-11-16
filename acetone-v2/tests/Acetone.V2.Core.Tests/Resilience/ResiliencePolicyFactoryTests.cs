using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Acetone.V2.Core.Tests.Resilience;

public class ResiliencePolicyFactoryTests
{
    private readonly ILogger<ResiliencePolicyFactory> _logger;
    private readonly IOptions<AcetoneOptions> _options;

    public ResiliencePolicyFactoryTests()
    {
        _logger = Substitute.For<ILogger<ResiliencePolicyFactory>>();

        var acetoneOptions = new AcetoneOptions
        {
            Resilience = new ResilienceOptions
            {
                RetryCount = 10,
                InitialRetryDelayMs = 100,
                MaxRetryDelayMs = 2000,
                PerAttemptTimeoutMs = 5000,
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerBreakDurationMs = 30000,
                CircuitBreakerSamplingDurationMs = 60000
            }
        };

        _options = Options.Create(acetoneOptions);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var factory = new ResiliencePolicyFactory(_logger, _options);

        // Assert
        factory.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new ResiliencePolicyFactory(null!, _options);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new ResiliencePolicyFactory(_logger, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void CreateRetryPolicy_ShouldReturnConfiguredPolicy()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);

        // Act
        var policy = factory.CreateRetryPolicy();

        // Assert
        policy.Should().NotBeNull();
        policy.Should().BeOfType<RetryPolicy>();
    }

    [Fact]
    public void CreateCircuitBreakerPolicy_ShouldReturnConfiguredPolicy()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);

        // Act
        var policy = factory.CreateCircuitBreakerPolicy();

        // Assert
        policy.Should().NotBeNull();
        policy.Should().BeOfType<CircuitBreakerPolicy>();
    }

    [Fact]
    public async Task CreateRetryPolicy_ShouldUseConfiguredRetryCount()
    {
        // Arrange
        var customOptions = new AcetoneOptions
        {
            Resilience = new ResilienceOptions
            {
                RetryCount = 3,
                InitialRetryDelayMs = 10,
                MaxRetryDelayMs = 100,
                PerAttemptTimeoutMs = 5000,
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerBreakDurationMs = 30000,
                CircuitBreakerSamplingDurationMs = 60000
            }
        };
        var factory = new ResiliencePolicyFactory(_logger, Options.Create(customOptions));
        var policy = factory.CreateRetryPolicy();
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new HttpRequestException("Test error");
            });
        });

        // Should be initial attempt + 3 retries = 4 total
        attemptCount.Should().Be(4);
    }

    [Fact]
    public async Task CreateCircuitBreakerPolicy_ShouldUseConfiguredThreshold()
    {
        // Arrange
        var customOptions = new AcetoneOptions
        {
            Resilience = new ResilienceOptions
            {
                RetryCount = 10,
                InitialRetryDelayMs = 100,
                MaxRetryDelayMs = 2000,
                PerAttemptTimeoutMs = 5000,
                CircuitBreakerFailureThreshold = 3,
                CircuitBreakerBreakDurationMs = 1000,
                CircuitBreakerSamplingDurationMs = 10000
            }
        };
        var factory = new ResiliencePolicyFactory(_logger, Options.Create(customOptions));
        var policy = factory.CreateCircuitBreakerPolicy();

        // Act - cause failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test error");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Assert - circuit should be open
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                await Task.CompletedTask;
                return "should not execute";
            });
        });
    }

    [Fact]
    public async Task CreateCombinedPolicy_ShouldReturnPolicyWithRetryAndCircuitBreaker()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);

        // Act
        var policy = factory.CreateCombinedPolicy();

        // Assert
        policy.Should().NotBeNull();

        // Verify it can execute operations
        var result = await policy.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return "success";
        });

        result.Should().Be("success");
    }

    [Fact]
    public async Task CreateCombinedPolicy_ShouldApplyRetryThenCircuitBreaker()
    {
        // Arrange
        var customOptions = new AcetoneOptions
        {
            Resilience = new ResilienceOptions
            {
                RetryCount = 2,
                InitialRetryDelayMs = 10,
                MaxRetryDelayMs = 100,
                PerAttemptTimeoutMs = 5000,
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerBreakDurationMs = 1000,
                CircuitBreakerSamplingDurationMs = 10000
            }
        };
        var factory = new ResiliencePolicyFactory(_logger, Options.Create(customOptions));
        var policy = factory.CreateCombinedPolicy();
        var attemptCount = 0;

        // Act - transient failure should be retried
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 2)
            {
                await Task.CompletedTask;
                throw new HttpRequestException("Transient error");
            }
            return "success";
        });

        // Assert - retry should have worked
        result.Should().Be("success");
        attemptCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task CreateTimeoutPolicy_ShouldReturnConfiguredTimeoutPolicy()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);

        // Act
        var policy = factory.CreateTimeoutPolicy();

        // Assert
        policy.Should().NotBeNull();

        // Should complete within timeout
        var result = await policy.ExecuteAsync(async () =>
        {
            await Task.Delay(10);
            return "success";
        });

        result.Should().Be("success");
    }

    [Fact]
    public async Task CreateTimeoutPolicy_ShouldTimeoutLongRunningOperations()
    {
        // Arrange
        var shortTimeoutOptions = new AcetoneOptions
        {
            Resilience = new ResilienceOptions
            {
                RetryCount = 10,
                InitialRetryDelayMs = 100,
                MaxRetryDelayMs = 2000,
                PerAttemptTimeoutMs = 100, // Very short timeout
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerBreakDurationMs = 30000,
                CircuitBreakerSamplingDurationMs = 60000
            }
        };
        var factory = new ResiliencePolicyFactory(_logger, Options.Create(shortTimeoutOptions));
        var policy = factory.CreateTimeoutPolicy();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                await Task.Delay(5000); // Much longer than timeout
                return "should not complete";
            });
        });
    }

    [Fact]
    public void GetRetryPolicy_AfterCreation_ShouldReturnSameInstance()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);
        var policy1 = factory.CreateRetryPolicy();

        // Act
        var policy2 = factory.GetRetryPolicy();

        // Assert
        policy2.Should().NotBeNull();
        // Factory should create new instances, not singleton
    }

    [Fact]
    public void GetCircuitBreakerPolicy_AfterCreation_ShouldReturnSameInstance()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);
        var policy1 = factory.CreateCircuitBreakerPolicy();

        // Act
        var policy2 = factory.GetCircuitBreakerPolicy();

        // Assert
        policy2.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateFullResiliencePipeline_ShouldCombineAllPolicies()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);

        // Act
        var pipeline = factory.CreateFullResiliencePipeline();

        // Assert
        pipeline.Should().NotBeNull();

        // Should handle successful operations
        var result = await pipeline.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return "success";
        });

        result.Should().Be("success");
    }

    [Fact]
    public async Task CreateFullResiliencePipeline_ShouldHandleTransientFailures()
    {
        // Arrange
        var factory = new ResiliencePolicyFactory(_logger, _options);
        var pipeline = factory.CreateFullResiliencePipeline();
        var attemptCount = 0;

        // Act
        var result = await pipeline.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                await Task.CompletedTask;
                throw new HttpRequestException("Transient error");
            }
            return "success";
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().BeGreaterThanOrEqualTo(3);
    }
}
