using Prometheus;

namespace Acetone.V2.Proxy.Metrics;

/// <summary>
/// Custom Prometheus metrics for Acetone proxy.
/// </summary>
public class AcetoneMetrics
{
    private readonly Counter _urlResolutionsTotal;
    private readonly Histogram _urlResolutionDuration;
    private readonly Counter _cacheHitsTotal;
    private readonly Counter _cacheMissesTotal;
    private readonly Counter _serviceFabricApiCallsTotal;
    private readonly Histogram _serviceFabricApiDuration;
    private readonly Gauge _circuitBreakerState;

    public AcetoneMetrics()
    {
        _urlResolutionsTotal = Prometheus.Metrics.CreateCounter(
            "acetone_url_resolutions_total",
            "Total number of URL resolutions",
            new CounterConfiguration
            {
                LabelNames = new[] { "status" }
            });

        _urlResolutionDuration = Prometheus.Metrics.CreateHistogram(
            "acetone_url_resolution_duration_seconds",
            "Duration of URL resolution operations in seconds",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
            });

        _cacheHitsTotal = Prometheus.Metrics.CreateCounter(
            "acetone_cache_hits_total",
            "Total number of cache hits");

        _cacheMissesTotal = Prometheus.Metrics.CreateCounter(
            "acetone_cache_misses_total",
            "Total number of cache misses");

        _serviceFabricApiCallsTotal = Prometheus.Metrics.CreateCounter(
            "acetone_service_fabric_api_calls_total",
            "Total number of Service Fabric API calls",
            new CounterConfiguration
            {
                LabelNames = new[] { "operation" }
            });

        _serviceFabricApiDuration = Prometheus.Metrics.CreateHistogram(
            "acetone_service_fabric_api_duration_seconds",
            "Duration of Service Fabric API calls in seconds",
            new HistogramConfiguration
            {
                LabelNames = new[] { "operation" },
                Buckets = Histogram.ExponentialBuckets(0.001, 2, 10)
            });

        _circuitBreakerState = Prometheus.Metrics.CreateGauge(
            "acetone_circuit_breaker_state",
            "Circuit breaker state (0=Closed, 1=Open, 2=HalfOpen)",
            new GaugeConfiguration
            {
                LabelNames = new[] { "service", "state" }
            });
    }

    public void IncrementUrlResolutions(string status)
    {
        _urlResolutionsTotal.WithLabels(status).Inc();
    }

    public void RecordUrlResolutionDuration(double durationSeconds)
    {
        _urlResolutionDuration.Observe(durationSeconds);
    }

    public void IncrementCacheHits()
    {
        _cacheHitsTotal.Inc();
    }

    public void IncrementCacheMisses()
    {
        _cacheMissesTotal.Inc();
    }

    public void IncrementServiceFabricApiCalls(string operation)
    {
        _serviceFabricApiCallsTotal.WithLabels(operation).Inc();
    }

    public void RecordServiceFabricApiDuration(string operation, double durationSeconds)
    {
        _serviceFabricApiDuration.WithLabels(operation).Observe(durationSeconds);
    }

    public void SetCircuitBreakerState(string service, string state)
    {
        var stateValue = state switch
        {
            "Closed" => 0,
            "Open" => 1,
            "HalfOpen" => 2,
            _ => -1
        };

        _circuitBreakerState.WithLabels(service, state).Set(stateValue);
    }
}
