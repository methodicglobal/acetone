using Acetone.V2.Core.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace Acetone.V2.Core.Tests.Diagnostics;

public class AcetoneTelemetryTests
{
    [Fact]
    public void Instruments_AreCreated_Correctly()
    {
        using var telemetry = new AcetoneTelemetry();
        
        Assert.NotNull(telemetry.UrlResolutionsTotal);
        Assert.NotNull(telemetry.UrlResolutionDuration);
        Assert.NotNull(telemetry.CacheHitsTotal);
        Assert.NotNull(telemetry.CacheMissesTotal);
        Assert.NotNull(telemetry.ServiceFabricApiCallsTotal);
        Assert.NotNull(telemetry.ServiceFabricApiDuration);
        
        Assert.Equal("acetone_url_resolutions_total", telemetry.UrlResolutionsTotal.Name);
    }

    [Fact]
    public void Recording_Metrics_Works()
    {
        using var telemetry = new AcetoneTelemetry();
        
        long recordedValue = 0;
        string? recordedTag = null;
        
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AcetoneTelemetry.ServiceName && instrument.Name == "acetone_url_resolutions_total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            recordedValue = measurement;
            recordedTag = tags.ToArray().FirstOrDefault(t => t.Key == "status").Value?.ToString();
        });
        
        listener.Start();
        
        telemetry.UrlResolutionsTotal.Add(1, new KeyValuePair<string, object?>("status", "success"));
        
        // Force processing
        listener.RecordObservableInstruments(); // Not needed for Counter but good practice? No, Counter is pushed.
        
        Assert.Equal(1, recordedValue);
        Assert.Equal("success", recordedTag);
    }
}
