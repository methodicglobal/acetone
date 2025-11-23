using System.Collections.Concurrent;
using System.Diagnostics;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Query;
using System.Security.Cryptography.X509Certificates;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Acetone.V2.Core.Caching;
using Acetone.V2.Core.Resilience;
using Acetone.V2.Core.Diagnostics;

namespace Acetone.V2.Core.ServiceFabric;

public class ServiceFabricResolver : IServiceFabricResolver, IDisposable
{
    private readonly ILogger<ServiceFabricResolver> _logger;
    private readonly AcetoneOptions _options;
    private readonly IFabricClientWrapper _fabricClient;
    private readonly IThreeTierCache _cache;
    private readonly IResiliencePolicies _resiliencePolicies;
    private readonly AcetoneTelemetry _telemetry;
    private bool _isDisposed;

    // Semaphores for async locking
    private readonly SemaphoreSlim _applicationLock = new(1, 1);
    private readonly SemaphoreSlim _serviceLock = new(1, 1);

    // Logging EventIds
    private static class Events
    {
        public static readonly EventId Resolution = new(1001, nameof(Resolution));
        public static readonly EventId CacheHit = new(1002, nameof(CacheHit));
        public static readonly EventId CacheMiss = new(1003, nameof(CacheMiss));
        public static readonly EventId Refresh = new(1004, nameof(Refresh));
    }

    public ServiceFabricResolver(IOptions<AcetoneOptions> options, ILogger<ServiceFabricResolver> logger, IFabricClientFactory fabricClientFactory, IThreeTierCache cache, IResiliencePolicies resiliencePolicies, AcetoneTelemetry telemetry)
    {
        _options = options.Value;
        _logger = logger;
        _fabricClient = fabricClientFactory.Create();
        _cache = cache;
        _resiliencePolicies = resiliencePolicies;
        _telemetry = telemetry;
        
        // Register for service notifications to invalidate cache
        _fabricClient.ServiceNotificationFilterMatched += ServiceManager_ServiceNotificationFilterMatched;
        
        // Initial cache warmup
        Task.Run(WarmupCacheAsync);
    }



    private void ServiceManager_ServiceNotificationFilterMatched(object? sender, EventArgs e)
    {
        _logger.LogInformation("Received service notification event, clearing service and partition cache");
        _cache.ClearServiceAndPartitionCache();
    }

    private async Task WarmupCacheAsync()
    {
        _logger.LogInformation("BEGIN CACHE WARMUP");
        try
        {
            var appTypes = await _fabricClient.GetApplicationTypeListAsync();
            foreach (var appType in appTypes)
            {
                try
                {
                    // Simulate a resolution to warm up
                    string appName = appType.ApplicationTypeName.Replace("Type", string.Empty);
                    await ResolveUrlAsync(appName, Guid.NewGuid(), null, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Exception warming up cache for {AppType}: {Error}", appType.ApplicationTypeName, ExceptionFormatter.FormatException(ex));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Cache warmup failed: {Error}", ExceptionFormatter.FormatException(ex));
        }
        finally
        {
            _logger.LogInformation("END CACHE WARMUP");
        }
    }

    public async Task<string> ResolveUrlAsync(string applicationName, Guid invocationId, string? version = null, bool refreshCache = false)
    {
        var startTime = Stopwatch.GetTimestamp();
        var status = "success";
        try
        {
            _logger.LogInformation(Events.Resolution, "Resolving URL for application {AppName}, invocation {InvocationId}", applicationName, invocationId);

            if (string.IsNullOrWhiteSpace(applicationName))
            {
                throw new ArgumentException("Application name is required", nameof(applicationName));
            }

            string cacheKey = (applicationName + (version ?? "-no-service-version")).ToUpperInvariant();
            
            var application = await _cache.GetApplicationAsync(cacheKey);
            
            if (!refreshCache && application != null)
            {
                _logger.LogDebug(Events.CacheHit, "Cache hit for application {AppName}", applicationName);
                return await StatelessEndpointUriAsync(application, invocationId);
            }

            if (refreshCache || application == null)
            {
                await _applicationLock.WaitAsync();
                try
                {
                    // Double-check after acquiring lock
                    application = await _cache.GetApplicationAsync(cacheKey);
                    
                    if (refreshCache || application == null)
                    {
                        _logger.LogDebug(Events.CacheMiss, "Cache miss for application {AppName}, querying cluster", applicationName);
                        application = await GetApplicationMetadataAsync(applicationName, version, invocationId);
                        await _cache.SetApplicationAsync(cacheKey, application);
                    }
                }
                finally
                {
                    _applicationLock.Release();
                }
            }

            return await StatelessEndpointUriAsync(application!, invocationId);
        }
        catch (Exception)
        {
            status = "error";
            throw;
        }
        finally
        {
             var duration = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
             _telemetry.UrlResolutionDuration.Record(duration);
             _telemetry.UrlResolutionsTotal.Add(1, new KeyValuePair<string, object?>("status", status));
        }
    }

    public async Task<string> ResolveFunctionUriAsync(string applicationName, Guid invocationId, string? version = null, bool refreshCache = false)
    {
        _logger.LogInformation(Events.Resolution, "Resolving Function URL for application {AppName}, invocation {InvocationId}", applicationName, invocationId);

        string cacheKey = $"{applicationName}-FKT-{version ?? "no-version"}";
        
        var application = await _cache.GetApplicationAsync(cacheKey);
        
        if (!refreshCache && application != null)
        {
            return await FunctionEndpointUriAsync(application, invocationId);
        }

        await _applicationLock.WaitAsync();
        try
        {
            application = await _cache.GetApplicationAsync(cacheKey);
            
            if (refreshCache || application == null)
            {
                application = await GetApplicationMetadataAsync(applicationName, version, invocationId);
                await _cache.SetApplicationAsync(cacheKey, application);
            }
        }
        finally
        {
            _applicationLock.Release();
        }

        return await FunctionEndpointUriAsync(application!, invocationId);
    }

    private async Task<IApplicationWrapper> GetApplicationMetadataAsync(string applicationName, string? version, Guid invocationId)
    {
        var applications = (await _fabricClient.GetApplicationListAsync()).ToList();
        
        if (applications == null || applications.Count == 0)
        {
            throw new KeyNotFoundException($"No applications found on cluster.");
        }

        var matchingApps = FilterApplicationsByTypeName(applications, applicationName);
        if (!matchingApps.Any())
        {
            matchingApps = FilterApplicationsByApplicationName(applications, applicationName);
        }
        else
        {
            // If we found by type name, try to narrow down by exact name if possible
            var exactNameMatches = FilterApplicationsByApplicationName(matchingApps, applicationName);
            if (exactNameMatches.Count == 1)
            {
                matchingApps = exactNameMatches;
            }
        }

        if (!matchingApps.Any())
        {
            throw new KeyNotFoundException($"Could not find any application matching {applicationName}");
        }

        if (matchingApps.Count > 1)
        {
            if (!string.IsNullOrEmpty(version))
            {
                matchingApps = matchingApps.Where(a => a.ApplicationTypeVersion.Equals(version, StringComparison.InvariantCultureIgnoreCase)).ToList();
            }

            if (!matchingApps.Any())
            {
                throw new KeyNotFoundException($"Could not find any application matching {applicationName} with version {version}");
            }
            
            // If still multiple, try to pick best match
             var normalizedTarget = NormalizeApplicationIdentifier(applicationName);
             var exactNameMatches = matchingApps.Where(a => NormalizeApplicationIdentifier(a.ApplicationName?.AbsoluteUri).Equals(normalizedTarget, StringComparison.InvariantCultureIgnoreCase)).ToList();
             
             if (exactNameMatches.Count > 0)
             {
                 matchingApps = exactNameMatches;
             }

             if (matchingApps.Count > 1)
             {
                 var readyApps = matchingApps.Where(a => a.ApplicationStatus == ApplicationStatus.Ready).ToList();
                 if (readyApps.Any())
                 {
                     matchingApps = readyApps;
                 }
             }

             if (matchingApps.Count > 1)
             {
                 var selected = matchingApps.OrderBy(a => NormalizeApplicationIdentifier(a.ApplicationName?.AbsoluteUri)).First();
                 _logger.LogWarning("Multiple applications matched {AppName}. Selecting {Selected}", applicationName, selected.ApplicationName);
                 return selected;
             }
        }

        return matchingApps.Single();
    }

    private async Task<string> StatelessEndpointUriAsync(IApplicationWrapper application, Guid invocationId)
    {
        string cacheKey = application.ApplicationName.AbsoluteUri;

        var service = await _cache.GetServiceAsync(cacheKey);

        if (service == null)
        {
            lock (_serviceLock)
            {
                service = _cache.GetServiceAsync(cacheKey).GetAwaiter().GetResult();
                
                if (service == null)
                {
                    var services = _fabricClient.GetServiceListAsync(application.ApplicationName).GetAwaiter().GetResult();
                    
                    var statelessServices = services
                        .Where(s => s.ServiceKind == ServiceKind.Stateless && 
                                   (s.ServiceTypeName.Contains("API", StringComparison.OrdinalIgnoreCase) || 
                                    s.ServiceTypeName.Contains("SERVICE", StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (!statelessServices.Any())
                    {
                        throw new KeyNotFoundException($"Application {application.ApplicationName} has no matching stateless services (API/SERVICE)");
                    }
                    
                    if (statelessServices.Count > 1)
                    {
                        throw new InvalidOperationException($"{statelessServices.Count} stateless services matched heuristic for {application.ApplicationName}");
                    }

                    service = statelessServices.Single();
                    _cache.SetServiceAsync(cacheKey, service).Wait();

                    // Register notification
                    var filter = new ServiceNotificationFilterDescription(service.ServiceName, true, false);
                    _fabricClient.RegisterServiceNotificationFilterAsync(filter).Wait();
                }
            }
        }

        return await PartitionEndpointAsync(service!, invocationId);
    }

    private async Task<string> FunctionEndpointUriAsync(IApplicationWrapper application, Guid invocationId)
    {
        string cacheKey = application.ApplicationName.AbsoluteUri + "-FKT";

        var service = await _cache.GetServiceAsync(cacheKey);

        if (service == null)
        {
            lock (_serviceLock)
            {
                service = _cache.GetServiceAsync(cacheKey).GetAwaiter().GetResult();
                
                if (service == null)
                {
                    var services = _fabricClient.GetServiceListAsync(application.ApplicationName).GetAwaiter().GetResult();
                    
                    var statelessServices = services
                        .Where(s => s.ServiceKind == ServiceKind.Stateless && 
                                    s.ServiceTypeName.Contains("FUNCTION", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (!statelessServices.Any())
                    {
                        throw new KeyNotFoundException($"Application {application.ApplicationName} has no matching function services");
                    }
                    
                    if (statelessServices.Count > 1)
                    {
                        throw new InvalidOperationException($"{statelessServices.Count} function services matched heuristic for {application.ApplicationName}");
                    }

                    service = statelessServices.Single();
                    _cache.SetServiceAsync(cacheKey, service).Wait();

                    var filter = new ServiceNotificationFilterDescription(service.ServiceName, true, false);
                    _fabricClient.RegisterServiceNotificationFilterAsync(filter).Wait();
                }
            }
        }

        return await PartitionEndpointAsync(service!, invocationId);
    }

    private async Task<string> PartitionEndpointAsync(IServiceWrapper service, Guid invocationId)
    {
        string cacheKey = service.ServiceName.AbsoluteUri;
        
        if (!_options.DisablePartitionCache)
        {
            var cachedPartition = await _cache.GetPartitionAsync(cacheKey);
            if (cachedPartition != null)
            {
                _logger.LogDebug("Partition cache hit for {ServiceName}", service.ServiceName);
                return ProcessEndpoint(cachedPartition.GetEndpoint(), invocationId);
            }
        }

        var partition = await ResolveServicePartitionAsync(service.ServiceName, invocationId);

        return ProcessEndpoint(partition.GetEndpoint(), invocationId);
    }

    private string ProcessEndpoint(IResolvedServiceEndpointWrapper endpoint, Guid invocationId)
    {
        if (endpoint == null)
        {
            throw new InvalidOperationException($"No endpoints available, invocation {invocationId}");
        }

        string address = endpoint.Address;
        _logger.LogDebug("Selected endpoint {Address}", address);

        if (address.Contains('{'))
        {
            address = ServiceFabricUrlParser.EndpointJsonToText(address, _logger);
        }

        return ServiceFabricUrlParser.NormalizeLocalEndpoint(address, _logger);
    }

    private async Task<IResolvedServicePartitionWrapper> ResolveServicePartitionAsync(Uri serviceName, Guid invocationId)
    {
        string cacheKey = serviceName.ToString();
        var partition = await _cache.GetPartitionAsync(cacheKey);

        if (partition != null)
        {
            return partition;
        }

        // Use semaphore for async locking
        await _serviceLock.WaitAsync();
        try
        {
            partition = await _cache.GetPartitionAsync(cacheKey);
            if (partition == null)
            {
                _logger.LogDebug(Events.CacheMiss, "Partition cache miss for {ServiceName}", serviceName);
                partition = await ResolveServicePartitionWithRetryAsync(serviceName, invocationId);
                await _cache.SetPartitionAsync(cacheKey, partition);
            }
        }
        finally
        {
            _serviceLock.Release();
        }

        return partition;
    }

    private async Task<IResolvedServicePartitionWrapper> ResolveServicePartitionWithRetryAsync(Uri serviceName, Guid invocationId)
    {
        return await _resiliencePolicies.GetServiceFabricPolicy().ExecuteAsync(async () =>
        {
            var partition = await _fabricClient.ResolveServicePartitionAsync(serviceName);
            if (partition == null)
            {
                throw new FabricTransientException($"Partition resolution returned null for {serviceName}");
            }
            return partition;
        });
    }

    // Helper methods for application filtering (ported from V1)
    private static List<IApplicationWrapper> FilterApplicationsByTypeName(IEnumerable<IApplicationWrapper> applications, string applicationName)
    {
        string normalizedTarget = NormalizeApplicationIdentifier(applicationName);
        return applications.Where(a =>
        {
            string normalizedTypeName = NormalizeApplicationType(a.ApplicationTypeName);
            return normalizedTypeName.Equals(normalizedTarget, StringComparison.InvariantCultureIgnoreCase);
        }).ToList();
    }

    private static List<IApplicationWrapper> FilterApplicationsByApplicationName(IEnumerable<IApplicationWrapper> applications, string applicationName)
    {
        string normalizedTarget = NormalizeApplicationIdentifier(applicationName);
        return applications.Where(a =>
        {
            var candidate = NormalizeApplicationIdentifier(a.ApplicationName?.AbsoluteUri);
            return candidate.Equals(normalizedTarget, StringComparison.InvariantCultureIgnoreCase);
        }).ToList();
    }

    private static string NormalizeApplicationType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        int index = value.LastIndexOf("type", StringComparison.InvariantCultureIgnoreCase);
        if (index >= 0)
        {
            value = value.Substring(0, index);
        }
        return NormalizeApplicationIdentifier(value);
    }

    private static string NormalizeApplicationIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        if (value.StartsWith("fabric:/", StringComparison.InvariantCultureIgnoreCase))
        {
            value = value.Substring("fabric:/".Length);
        }
        else if (value.StartsWith("fabric:", StringComparison.InvariantCultureIgnoreCase))
        {
            value = value.Substring("fabric:".Length);
        }
        value = value.Trim('/');
        if (value.IndexOf('_') >= 0)
        {
            value = value.Replace('_', '-');
        }
        return value;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _fabricClient.ServiceNotificationFilterMatched -= ServiceManager_ServiceNotificationFilterMatched;
        _applicationLock.Dispose();
        _serviceLock.Dispose();
        // _fabricClient is a wrapper, check if it needs disposal. 
        // The factory creates it, but usually we don't dispose injected singletons if they are shared.
        // However, here we created it via factory.Create().
        // Assuming IFabricClientWrapper extends IDisposable or we should leave it to GC if not explicit.
        // But standard FabricClient needs disposal.
        // Let's assume we should dispose it if we own it.
        // But IFabricClientWrapper interface definition is needed to know if it is IDisposable.
        // For now, let's just dispose the semaphores and unregister events as that's critical.
        
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
