using System.Collections.Concurrent;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.ServiceFabric;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Acetone.V2.Core.Diagnostics;

namespace Acetone.V2.Core.Caching;

public class ThreeTierCache : IThreeTierCache, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly AcetoneOptions _options;
    private readonly ILogger<ThreeTierCache> _logger;

    // Tier 1 & 2: Application and Service caches (Long lived, manual invalidation)
    // We use ConcurrentDictionary for these to allow easy and explicit clearing.
    private readonly ConcurrentDictionary<string, IApplicationWrapper> _applicationCache = new();
    private readonly ConcurrentDictionary<string, IServiceWrapper> _serviceCache = new();

    // Tier 3: Partition Cache (Short lived, TTL, but also needs bulk invalidation)
    // We use IMemoryCache for TTL, and CancellationTokenSource for bulk invalidation.
    private CancellationTokenSource _partitionCacheTokenSource = new();
    private readonly object _tokenLock = new();
    private bool _isDisposed;

    private readonly AcetoneTelemetry _telemetry;

    public ThreeTierCache(IMemoryCache memoryCache, IOptions<AcetoneOptions> options, ILogger<ThreeTierCache> logger, AcetoneTelemetry telemetry)
    {
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
        _telemetry = telemetry;
    }

    public Task<IApplicationWrapper?> GetApplicationAsync(string key)
    {
        if (_applicationCache.TryGetValue(key, out var app))
        {
            _telemetry.CacheHitsTotal.Add(1, new KeyValuePair<string, object?>("cache_tier", "application"));
            return Task.FromResult<IApplicationWrapper?>(app);
        }
        _telemetry.CacheMissesTotal.Add(1, new KeyValuePair<string, object?>("cache_tier", "application"));
        return Task.FromResult<IApplicationWrapper?>(null);
    }

    public Task SetApplicationAsync(string key, IApplicationWrapper application)
    {
        _applicationCache[key] = application;
        return Task.CompletedTask;
    }

    public Task<IServiceWrapper?> GetServiceAsync(string key)
    {
        if (_serviceCache.TryGetValue(key, out var service))
        {
            return Task.FromResult<IServiceWrapper?>(service);
        }
        return Task.FromResult<IServiceWrapper?>(null);
    }

    public Task SetServiceAsync(string key, IServiceWrapper service)
    {
        _serviceCache[key] = service;
        return Task.CompletedTask;
    }

    public Task<IResolvedServicePartitionWrapper?> GetPartitionAsync(string key)
    {
        if (_options.DisablePartitionCache)
        {
            return Task.FromResult<IResolvedServicePartitionWrapper?>(null);
        }

        if (_memoryCache.TryGetValue(key, out IResolvedServicePartitionWrapper? partition))
        {
            _telemetry.CacheHitsTotal.Add(1, new KeyValuePair<string, object?>("cache_tier", "partition"));
            return Task.FromResult(partition);
        }
        _telemetry.CacheMissesTotal.Add(1, new KeyValuePair<string, object?>("cache_tier", "partition"));
        return Task.FromResult<IResolvedServicePartitionWrapper?>(null);
    }

    public Task SetPartitionAsync(string key, IResolvedServicePartitionWrapper partition)
    {
        if (_options.DisablePartitionCache)
        {
            return Task.CompletedTask;
        }

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.PartitionCacheTtlSeconds)
        };
        
        // Add expiration token for bulk invalidation
        options.AddExpirationToken(new CancellationChangeToken(_partitionCacheTokenSource.Token));

        _memoryCache.Set(key, partition, options);
        return Task.CompletedTask;
    }

    public void ClearServiceAndPartitionCache()
    {
        _logger.LogInformation("Clearing Service and Partition caches");
        
        _serviceCache.Clear();
        
        lock (_tokenLock)
        {
            if (!_partitionCacheTokenSource.IsCancellationRequested)
            {
                _partitionCacheTokenSource.Cancel();
                _partitionCacheTokenSource.Dispose();
                _partitionCacheTokenSource = new CancellationTokenSource();
            }
        }
    }

    public void ClearAll()
    {
        _logger.LogInformation("Clearing ALL caches");
        _applicationCache.Clear();
        ClearServiceAndPartitionCache();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            lock (_tokenLock)
            {
                _partitionCacheTokenSource.Cancel();
                _partitionCacheTokenSource.Dispose();
            }
        }
        _isDisposed = true;
    }
}
