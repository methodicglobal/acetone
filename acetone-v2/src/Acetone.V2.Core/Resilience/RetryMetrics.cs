namespace Acetone.V2.Core.Resilience;

/// <summary>
/// Metrics for retry policy operations.
/// </summary>
public class RetryMetrics
{
    /// <summary>
    /// Total number of retry attempts across all executions.
    /// </summary>
    public long TotalRetries { get; set; }

    /// <summary>
    /// Total number of policy executions.
    /// </summary>
    public long TotalExecutions { get; set; }

    /// <summary>
    /// Total number of successful executions after retries.
    /// </summary>
    public long SuccessfulExecutions { get; set; }

    /// <summary>
    /// Total number of failed executions (after all retries exhausted).
    /// </summary>
    public long FailedExecutions { get; set; }

    /// <summary>
    /// Average retry count per execution.
    /// </summary>
    public double AverageRetriesPerExecution =>
        TotalExecutions > 0 ? (double)TotalRetries / TotalExecutions : 0;
}
