using Acetone.V2.Core.Caching;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.ServiceFabric;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Acetone.V2.Core.Diagnostics;

namespace Acetone.V2.Core.Tests;

public class ThreeTierCacheTests
{
    private readonly IMemoryCache _memoryCache;
    private readonly IOptions<AcetoneOptions> _options;
    private readonly ThreeTierCache _cache;

    public ThreeTierCacheTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = new AcetoneOptions
        {
            PartitionCacheTtlSeconds = 1, // Short TTL for testing
            DisablePartitionCache = false
        };
        _options = Options.Create(options);
        _cache = new ThreeTierCache(_memoryCache, _options, NullLogger<ThreeTierCache>.Instance, new AcetoneTelemetry());
    }

    [Fact]
    public async Task ApplicationCache_StoresAndRetrievesValue()
    {
        var key = "app-key";
        var app = Substitute.For<IApplicationWrapper>();
        
        await _cache.SetApplicationAsync(key, app);
        var result = await _cache.GetApplicationAsync(key);
        
        Assert.Same(app, result);
    }

    [Fact]
    public async Task ServiceCache_StoresAndRetrievesValue()
    {
        var key = "service-key";
        var service = Substitute.For<IServiceWrapper>();
        
        await _cache.SetServiceAsync(key, service);
        var result = await _cache.GetServiceAsync(key);
        
        Assert.Same(service, result);
    }

    [Fact]
    public async Task PartitionCache_StoresAndRetrievesValue()
    {
        var key = "partition-key";
        var partition = Substitute.For<IResolvedServicePartitionWrapper>();
        
        await _cache.SetPartitionAsync(key, partition);
        var result = await _cache.GetPartitionAsync(key);
        
        Assert.Same(partition, result);
    }

    [Fact]
    public async Task PartitionCache_ExpiresAfterTtl()
    {
        var key = "partition-ttl-key";
        var partition = Substitute.For<IResolvedServicePartitionWrapper>();
        
        await _cache.SetPartitionAsync(key, partition);
        
        // Wait for expiration (TTL is 1s)
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        
        var result = await _cache.GetPartitionAsync(key);
        
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearServiceAndPartitionCache_ClearsServiceAndPartition_KeepsApplication()
    {
        var appKey = "app-key";
        var serviceKey = "service-key";
        var partitionKey = "partition-key";

        var app = Substitute.For<IApplicationWrapper>();
        var service = Substitute.For<IServiceWrapper>();
        var partition = Substitute.For<IResolvedServicePartitionWrapper>();

        await _cache.SetApplicationAsync(appKey, app);
        await _cache.SetServiceAsync(serviceKey, service);
        await _cache.SetPartitionAsync(partitionKey, partition);

        _cache.ClearServiceAndPartitionCache();

        Assert.NotNull(await _cache.GetApplicationAsync(appKey));
        Assert.Null(await _cache.GetServiceAsync(serviceKey));
        Assert.Null(await _cache.GetPartitionAsync(partitionKey));
    }

    [Fact]
    public async Task ClearAll_ClearsEverything()
    {
        var appKey = "app-key";
        var serviceKey = "service-key";
        var partitionKey = "partition-key";

        var app = Substitute.For<IApplicationWrapper>();
        var service = Substitute.For<IServiceWrapper>();
        var partition = Substitute.For<IResolvedServicePartitionWrapper>();

        await _cache.SetApplicationAsync(appKey, app);
        await _cache.SetServiceAsync(serviceKey, service);
        await _cache.SetPartitionAsync(partitionKey, partition);

        _cache.ClearAll();

        Assert.Null(await _cache.GetApplicationAsync(appKey));
        Assert.Null(await _cache.GetServiceAsync(serviceKey));
        Assert.Null(await _cache.GetPartitionAsync(partitionKey));
    }
    
    [Fact]
    public async Task PartitionCache_ReturnsNull_WhenDisabled()
    {
        var options = new AcetoneOptions
        {
            DisablePartitionCache = true
        };
        var cache = new ThreeTierCache(_memoryCache, Options.Create(options), NullLogger<ThreeTierCache>.Instance, new AcetoneTelemetry());
        
        var key = "partition-key";
        var partition = Substitute.For<IResolvedServicePartitionWrapper>();
        
        await cache.SetPartitionAsync(key, partition);
        var result = await cache.GetPartitionAsync(key);
        
        Assert.Null(result);
    }
}
