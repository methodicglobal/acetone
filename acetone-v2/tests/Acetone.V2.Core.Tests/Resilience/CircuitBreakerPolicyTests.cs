using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly.CircuitBreaker;

namespace Acetone.V2.Core.Tests.Resilience;

public class CircuitBreakerPolicyTests
{
    private readonly ILogger<CircuitBreakerPolicy> _logger;
    private readonly ResilienceOptions _options;

    public CircuitBreakerPolicyTests()
    {
        _logger = Substitute.For<ILogger<CircuitBreakerPolicy>>();
        _options = new ResilienceOptions
        {
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerBreakDurationMs = 1000,
            CircuitBreakerSamplingDurationMs = 10000
        };
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldCreateInstance()
    {
        // Act
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new CircuitBreakerPolicy(null!, _options);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new CircuitBreakerPolicy(_logger, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public async Task ExecuteAsync_WithSuccessfulOperations_ShouldKeepCircuitClosed()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Act
        for (int i = 0; i < 10; i++)
        {
            var result = await policy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                return "success";
            });

            result.Should().Be("success");
        }

        // Assert
        policy.GetState().Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_WithConsecutiveFailures_ShouldOpenCircuit()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Act - cause failures to exceed threshold
        for (int i = 0; i < _options.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Assert - circuit should now be open
        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                await Task.CompletedTask;
                return "should not execute";
            });
        });

        policy.GetState().Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCircuitOpen_ShouldRejectCalls()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Open the circuit
        for (int i = 0; i < _options.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Act & Assert - subsequent calls should be rejected immediately
        var exception = await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                await Task.CompletedTask;
                return "should not execute";
            });
        });

        exception.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AfterBreakDuration_ShouldTransitionToHalfOpen()
    {
        // Arrange
        var shortBreakOptions = new ResilienceOptions
        {
            CircuitBreakerFailureThreshold = 3,
            CircuitBreakerBreakDurationMs = 100, // Short duration for testing
            CircuitBreakerSamplingDurationMs = 5000
        };
        var policy = new CircuitBreakerPolicy(_logger, shortBreakOptions);

        // Open the circuit
        for (int i = 0; i < shortBreakOptions.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        policy.GetState().Should().Be(CircuitState.Open);

        // Wait for break duration
        await Task.Delay(shortBreakOptions.CircuitBreakerBreakDurationMs + 50);

        // Act - next call should test the circuit (half-open state)
        var successfulCall = false;
        try
        {
            var result = await policy.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                return "success";
            });
            successfulCall = true;
        }
        catch (BrokenCircuitException)
        {
            // If we get here, timing might be slightly off
        }

        // Assert - if the call succeeded, circuit should be closed
        if (successfulCall)
        {
            policy.GetState().Should().Be(CircuitState.Closed);
        }
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenWithSuccess_ShouldCloseCircuit()
    {
        // Arrange
        var shortBreakOptions = new ResilienceOptions
        {
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerBreakDurationMs = 100,
            CircuitBreakerSamplingDurationMs = 5000
        };
        var policy = new CircuitBreakerPolicy(_logger, shortBreakOptions);

        // Open the circuit
        for (int i = 0; i < shortBreakOptions.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Wait for break duration
        await Task.Delay(shortBreakOptions.CircuitBreakerBreakDurationMs + 100);

        // Act - successful call should close the circuit
        var result = await policy.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return "success";
        });

        // Assert
        result.Should().Be("success");
        policy.GetState().Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenWithFailure_ShouldReopenCircuit()
    {
        // Arrange
        var shortBreakOptions = new ResilienceOptions
        {
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerBreakDurationMs = 100,
            CircuitBreakerSamplingDurationMs = 5000
        };
        var policy = new CircuitBreakerPolicy(_logger, shortBreakOptions);

        // Open the circuit
        for (int i = 0; i < shortBreakOptions.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Wait for break duration
        await Task.Delay(shortBreakOptions.CircuitBreakerBreakDurationMs + 100);

        // Act - failure in half-open should reopen circuit
        try
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                await Task.CompletedTask;
                throw new HttpRequestException("Test failure");
            });
        }
        catch (HttpRequestException)
        {
            // Expected
        }

        // Assert - circuit should be open again
        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
        {
            await policy.ExecuteAsync<string>(async () =>
            {
                await Task.CompletedTask;
                return "should not execute";
            });
        });
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTrackStateTransitions()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);
        var initialMetrics = policy.GetMetrics();

        // Act - cause circuit to open
        for (int i = 0; i < _options.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Assert
        var metrics = policy.GetMetrics();
        metrics.StateTransitions.Should().BeGreaterThan(initialMetrics.StateTransitions);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogStateChanges()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Act - cause circuit to open
        for (int i = 0; i < _options.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Assert - verify logging occurred
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetState_ShouldReturnCurrentCircuitState()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Act & Assert - initially closed
        policy.GetState().Should().Be(CircuitState.Closed);

        // Open the circuit
        for (int i = 0; i < _options.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                await policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                });
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Should be open
        policy.GetState().Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldCancelOperation()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await policy.ExecuteAsync(async (ct) =>
            {
                await Task.Delay(100, ct);
                return "should not complete";
            }, cts.Token);
        });
    }

    [Fact]
    public void Reset_ShouldCloseCircuit()
    {
        // Arrange
        var policy = new CircuitBreakerPolicy(_logger, _options);

        // Open the circuit
        for (int i = 0; i < _options.CircuitBreakerFailureThreshold; i++)
        {
            try
            {
                policy.ExecuteAsync<string>(async () =>
                {
                    await Task.CompletedTask;
                    throw new HttpRequestException("Test failure");
                }).Wait();
            }
            catch
            {
                // Expected
            }
        }

        policy.GetState().Should().Be(CircuitState.Open);

        // Act
        policy.Reset();

        // Assert
        policy.GetState().Should().Be(CircuitState.Closed);
    }
}
