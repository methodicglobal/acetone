using Acetone.V2.Core.Caching;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Diagnostics;
using Acetone.V2.Core.ServiceFabric;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Fabric;
using System.Fabric.Query;

namespace Acetone.V2.Performance;

[MemoryDiagnoser]
public class CacheBenchmarks
{
    private ThreeTierCache _cache = null!;
    private IApplicationWrapper _app = null!;
    private IServiceWrapper _service = null!;
    private IResolvedServicePartitionWrapper _partition = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new AcetoneOptions 
        { 
            PartitionCacheTtlSeconds = 60
        });
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var telemetry = new AcetoneTelemetry();
        var logger = NullLogger<ThreeTierCache>.Instance;
        
        _cache = new ThreeTierCache(memoryCache, options, logger, telemetry);

        _app = new StubApp { ApplicationName = new Uri("fabric:/MyApp"), ApplicationTypeName = "MyAppType" };
        _service = new StubService { ServiceName = new Uri("fabric:/MyApp/MyService"), ServiceTypeName = "MyServiceTypeAPI" };
        _partition = new StubPartition { Endpoint = new StubEndpoint { Address = "http://localhost:8080/" } };
    }

    [Benchmark]
    public async Task SetAndGetApplication()
    {
        await _cache.SetApplicationAsync("MyApp", _app);
        await _cache.GetApplicationAsync("MyApp");
    }

    [Benchmark]
    public async Task SetAndGetPartition()
    {
        await _cache.SetPartitionAsync("MyApp/MyService", _partition);
        await _cache.GetPartitionAsync("MyApp/MyService");
    }
}
