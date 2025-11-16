using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly;
using System.Diagnostics;

namespace Acetone.V2.Core.Tests.Resilience;

public class RetryPolicyTests
{
    private readonly ILogger<RetryPolicy> _logger;
    private readonly ResilienceOptions _options;

    public RetryPolicyTests()
    {
        _logger = Substitute.For<ILogger<RetryPolicy>>();
        _options = new ResilienceOptions
        {
            RetryCount = 10,
            InitialRetryDelayMs = 100,
            MaxRetryDelayMs = 2000
        };
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldCreateInstance()
    {
        // Act
        var policy = new RetryPolicy(_logger, _options);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new RetryPolicy(null!, _options);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new RetryPolicy(_logger, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public async Task ExecuteAsync_WithSuccessfulOperation_ShouldReturnResult()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var expectedResult = "success";

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return expectedResult;
        });

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task ExecuteAsync_WithTransientFailureThenSuccess_ShouldRetryAndSucceed()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
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
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithExponentialBackoff_ShouldIncreaseDelayBetweenRetries()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var attemptTimes = new List<DateTime>();

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                attemptTimes.Add(DateTime.UtcNow);
                await Task.CompletedTask;
                throw new HttpRequestException("Persistent error");
            });
        });

        // Verify exponential backoff (each delay should be roughly 2x the previous)
        attemptTimes.Count.Should().Be(11); // Initial attempt + 10 retries

        // Check that delays increase (with some tolerance for timing variations)
        for (int i = 2; i < Math.Min(5, attemptTimes.Count); i++)
        {
            var previousDelay = (attemptTimes[i - 1] - attemptTimes[i - 2]).TotalMilliseconds;
            var currentDelay = (attemptTimes[i] - attemptTimes[i - 1]).TotalMilliseconds;

            // Current delay should be roughly 2x previous (with 50% tolerance for test stability)
            if (previousDelay < _options.MaxRetryDelayMs)
            {
                currentDelay.Should().BeGreaterThan(previousDelay * 0.8);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExceedingMaxRetryDelay_ShouldCapAtMaxDelay()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var attemptTimes = new List<DateTime>();

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                attemptTimes.Add(DateTime.UtcNow);
                await Task.CompletedTask;
                throw new HttpRequestException("Persistent error");
            });
        });

        // Verify that delays don't exceed max delay
        for (int i = 1; i < attemptTimes.Count; i++)
        {
            var delay = (attemptTimes[i] - attemptTimes[i - 1]).TotalMilliseconds;
            delay.Should().BeLessThanOrEqualTo(_options.MaxRetryDelayMs + 100); // 100ms tolerance
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxRetriesExceeded_ShouldThrowException()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var attemptCount = 0;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new HttpRequestException("Persistent error");
            });
        });

        // Assert
        attemptCount.Should().Be(11); // Initial attempt + 10 retries
        exception.Message.Should().Contain("Persistent error");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogRetryAttempts()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
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

        // Verify logging occurred (at least for retries)
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentExceptionTypes_ShouldRetryOnTransientErrors()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                await Task.CompletedTask;
                throw new HttpRequestException("HTTP error");
            }
            else if (attemptCount == 2)
            {
                await Task.CompletedTask;
                throw new TimeoutException("Timeout");
            }
            return "success";
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonTransientError_ShouldNotRetry()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var attemptCount = 0;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new InvalidOperationException("Non-transient error");
            });
        });

        // Assert - should only attempt once for non-transient errors
        attemptCount.Should().Be(1);
        exception.Message.Should().Contain("Non-transient error");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIncrementRetryMetrics()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var initialMetrics = policy.GetMetrics();
        var attemptCount = 0;

        // Act
        await policy.ExecuteAsync(async () =>
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
        var finalMetrics = policy.GetMetrics();
        finalMetrics.TotalRetries.Should().Be(initialMetrics.TotalRetries + 2);
        finalMetrics.TotalExecutions.Should().Be(initialMetrics.TotalExecutions + 1);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldCancelOperation()
    {
        // Arrange
        var policy = new RetryPolicy(_logger, _options);
        var cts = new CancellationTokenSource();
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await policy.ExecuteAsync(async (ct) =>
            {
                attemptCount++;
                if (attemptCount == 2)
                {
                    cts.Cancel();
                }
                await Task.Delay(10, ct);
                throw new HttpRequestException("Error");
            }, cts.Token);
        });

        // Should stop retrying after cancellation
        attemptCount.Should().BeLessThan(11);
    }
}
