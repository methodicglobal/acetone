namespace Acetone.V2.Core.Resilience;

/// <summary>
/// Metrics for circuit breaker policy operations.
/// </summary>
public class CircuitBreakerMetrics
{
    /// <summary>
    /// Number of times the circuit breaker state has transitioned.
    /// </summary>
    public long StateTransitions { get; set; }

    /// <summary>
    /// Number of times the circuit was opened.
    /// </summary>
    public long CircuitOpenedCount { get; set; }

    /// <summary>
    /// Number of times the circuit was closed.
    /// </summary>
    public long CircuitClosedCount { get; set; }

    /// <summary>
    /// Number of times the circuit entered half-open state.
    /// </summary>
    public long CircuitHalfOpenCount { get; set; }

    /// <summary>
    /// Number of calls rejected due to open circuit.
    /// </summary>
    public long RejectedCalls { get; set; }

    /// <summary>
    /// Timestamp of last state transition.
    /// </summary>
    public DateTime? LastStateTransition { get; set; }
}
