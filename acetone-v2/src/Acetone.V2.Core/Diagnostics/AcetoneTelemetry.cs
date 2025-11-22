using System.Diagnostics.Metrics;

namespace Acetone.V2.Core.Diagnostics;

public class AcetoneTelemetry : IDisposable
{
    public const string ServiceName = "Acetone.V2.Core";
    private readonly Meter _meter;

    public Counter<long> UrlResolutionsTotal { get; }
    public Histogram<double> UrlResolutionDuration { get; }
    public Counter<long> CacheHitsTotal { get; }
    public Counter<long> CacheMissesTotal { get; }
    public Counter<long> ServiceFabricApiCallsTotal { get; }
    public Histogram<double> ServiceFabricApiDuration { get; }

    public AcetoneTelemetry()
    {
        _meter = new Meter(ServiceName);

        UrlResolutionsTotal = _meter.CreateCounter<long>(
            "acetone_url_resolutions_total",
            description: "Total number of URL resolutions");

        UrlResolutionDuration = _meter.CreateHistogram<double>(
            "acetone_url_resolution_duration_seconds",
            unit: "s",
            description: "Duration of URL resolution");

        CacheHitsTotal = _meter.CreateCounter<long>(
            "acetone_cache_hits_total",
            description: "Total number of cache hits");

        CacheMissesTotal = _meter.CreateCounter<long>(
            "acetone_cache_misses_total",
            description: "Total number of cache misses");

        ServiceFabricApiCallsTotal = _meter.CreateCounter<long>(
            "acetone_service_fabric_api_calls_total",
            description: "Total number of Service Fabric API calls");

        ServiceFabricApiDuration = _meter.CreateHistogram<double>(
            "acetone_service_fabric_api_duration_seconds",
            unit: "s",
            description: "Duration of Service Fabric API calls");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
