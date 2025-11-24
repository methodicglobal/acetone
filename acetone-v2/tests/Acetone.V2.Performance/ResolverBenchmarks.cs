using Acetone.V2.Core.Caching;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Diagnostics;
using Acetone.V2.Core.Resilience;
using Acetone.V2.Core.ServiceFabric;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Health;
using System.Fabric.Query;

namespace Acetone.V2.Performance;

[MemoryDiagnoser]
public class ResolverBenchmarks
{
    private ServiceFabricResolver _resolver = null!;
    private StubCache _cache = null!;
    private StubClientWrapper _client = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new AcetoneOptions { CredentialsType = CredentialsType.Local });
        var logger = NullLogger<ServiceFabricResolver>.Instance;
        _client = new StubClientWrapper();
        var factory = new StubFactory(_client);
        _cache = new StubCache();
        var resilience = new StubResilience();
        var telemetry = new AcetoneTelemetry();

        _resolver = new ServiceFabricResolver(options, logger, factory, _cache, resilience, telemetry);
    }

    [Benchmark]
    public async Task ResolveUrl_CacheHit()
    {
        // Cache is pre-populated by StubCache logic or we ensure it hits
        // For this benchmark, we want to measure the overhead of the resolver when cache returns immediately.
        // StubCache returns null by default, so it's a miss.
        // We need to populate it.
        // But ServiceFabricResolver populates it after miss.
        // So first call is miss, subsequent are hits.
        // But BenchmarkDotNet runs many iterations.
        // If we want purely HIT, we should ensure cache always returns.
        _cache.ForceHit = true;
        await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());
    }

    [Benchmark]
    public async Task ResolveUrl_CacheMiss()
    {
        _cache.ForceHit = false;
        await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());
    }
}

public class StubFactory : IFabricClientFactory
{
    private readonly IFabricClientWrapper _client;
    public StubFactory(IFabricClientWrapper client) => _client = client;
    public IFabricClientWrapper Create() => _client;
}

public class StubClientWrapper : IFabricClientWrapper
{
    public void Dispose() { }

    public Task<IEnumerable<IApplicationTypeWrapper>> GetApplicationTypeListAsync() => Task.FromResult(Enumerable.Empty<IApplicationTypeWrapper>());
    
    public Task<IEnumerable<IApplicationWrapper>> GetApplicationListAsync()
    {
        var app = new StubApp { ApplicationName = new Uri("fabric:/MyApp"), ApplicationTypeName = "MyAppType" };
        return Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { app });
    }

    public Task<IEnumerable<IServiceWrapper>> GetServiceListAsync(Uri applicationName)
    {
        var service = new StubService { ServiceName = new Uri("fabric:/MyApp/MyService"), ServiceTypeName = "MyServiceTypeAPI", ServiceKind = ServiceKind.Stateless };
        return Task.FromResult<IEnumerable<IServiceWrapper>>(new[] { service });
    }

    public Task<IResolvedServicePartitionWrapper> ResolveServicePartitionAsync(Uri serviceName)
    {
        var endpoint = new StubEndpoint { Address = "http://localhost:8080/" };
        var partition = new StubPartition { Endpoint = endpoint };
        return Task.FromResult<IResolvedServicePartitionWrapper>(partition);
    }

    public Task RegisterServiceNotificationFilterAsync(ServiceNotificationFilterDescription description) => Task.CompletedTask;

#pragma warning disable CS0067
    public event EventHandler<EventArgs>? ServiceNotificationFilterMatched;
#pragma warning restore CS0067
}

public class StubApp : IApplicationWrapper
{
    public Uri ApplicationName { get; set; } = new Uri("fabric:/DefaultApp");
    public string ApplicationTypeName { get; set; } = "DefaultAppType";
    public ApplicationStatus ApplicationStatus => ApplicationStatus.Ready;
    public string ApplicationTypeVersion => "1.0.0";
    public IReadOnlyDictionary<string, string> ApplicationParameters => new Dictionary<string, string>();
    public HealthState HealthState => HealthState.Ok;
}

public class StubService : IServiceWrapper
{
    public Uri ServiceName { get; set; } = new Uri("fabric:/DefaultApp/DefaultService");
    public string ServiceTypeName { get; set; } = "DefaultServiceType";
    public string ServiceManifestVersion => "1.0.0";
    public ServiceKind ServiceKind { get; set; }
    public ServiceStatus ServiceStatus => ServiceStatus.Active;
    public bool IsServiceGroup => false;
    public HealthState HealthState => HealthState.Ok;
}

public class StubPartition : IResolvedServicePartitionWrapper
{
    public StubEndpoint Endpoint { get; set; } = new StubEndpoint();
    public IResolvedServiceEndpointWrapper GetEndpoint() => Endpoint;
}

public class StubEndpoint : IResolvedServiceEndpointWrapper
{
    public string Address { get; set; } = "http://localhost:8080/";
    public ServiceEndpointRole Role => ServiceEndpointRole.Stateless;
}

public class StubCache : IThreeTierCache
{
    public bool ForceHit { get; set; }

    public Task<IApplicationWrapper?> GetApplicationAsync(string key) => Task.FromResult<IApplicationWrapper?>(ForceHit ? new StubApp { ApplicationName = new Uri("fabric:/MyApp"), ApplicationTypeName = "MyAppType" } : null);
    public Task SetApplicationAsync(string key, IApplicationWrapper app) => Task.CompletedTask;

    public Task<IServiceWrapper?> GetServiceAsync(string key) => Task.FromResult<IServiceWrapper?>(ForceHit ? new StubService { ServiceName = new Uri("fabric:/MyApp/MyService"), ServiceTypeName = "MyServiceTypeAPI" } : null);
    public Task SetServiceAsync(string key, IServiceWrapper service) => Task.CompletedTask;

    public Task<IResolvedServicePartitionWrapper?> GetPartitionAsync(string key) => Task.FromResult<IResolvedServicePartitionWrapper?>(ForceHit ? new StubPartition { Endpoint = new StubEndpoint { Address = "http://localhost:8080/" } } : null);
    public Task SetPartitionAsync(string key, IResolvedServicePartitionWrapper partition) => Task.CompletedTask;

    public void ClearServiceAndPartitionCache() { }
    public void ClearAll() { }
}

public class StubResilience : IResiliencePolicies
{
    public IAsyncPolicy GetServiceFabricPolicy() => Policy.NoOpAsync();
}
