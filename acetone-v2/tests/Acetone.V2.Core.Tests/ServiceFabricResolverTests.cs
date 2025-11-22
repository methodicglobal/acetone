using System.Collections.Concurrent;
using System.Fabric.Description;
using System.Fabric.Query;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.ServiceFabric;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Acetone.V2.Core.Caching;
using Acetone.V2.Core.Resilience;
using Polly;
using Polly;
using Acetone.V2.Core.Diagnostics;
using System.Fabric;

namespace Acetone.V2.Core.Tests;

public class ServiceFabricResolverTests
{
    private readonly IFabricClientFactory _mockFactory;
    private readonly IFabricClientWrapper _mockClient;
    private readonly IThreeTierCache _mockCache;
    private readonly IResiliencePolicies _mockResilience;
    private readonly IOptions<AcetoneOptions> _options;
    private readonly ServiceFabricResolver _resolver;

    public ServiceFabricResolverTests()
    {
        _mockFactory = Substitute.For<IFabricClientFactory>();
        _mockClient = Substitute.For<IFabricClientWrapper>();
        _mockCache = Substitute.For<IThreeTierCache>();
        _mockResilience = Substitute.For<IResiliencePolicies>();
        
        _mockFactory.Create().Returns(_mockClient);
        
        // Mock resilience policy to execute immediately
        var policy = Policy.NoOpAsync();
        _mockResilience.GetServiceFabricPolicy().Returns(policy);

        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = new[] { "localhost:19000" },
            CredentialsType = CredentialsType.Local
        };
        _options = Options.Create(options);
        
        var telemetry = new AcetoneTelemetry();

        _resolver = new ServiceFabricResolver(_options, NullLogger<ServiceFabricResolver>.Instance, _mockFactory, _mockCache, _mockResilience, telemetry);
    }

    [Fact]
    public async Task ResolveUrlAsync_ReturnsCorrectEndpoint_WhenAppAndServiceExist()
    {
        // Arrange
        var appName = new Uri("fabric:/MyApp");
        var serviceName = new Uri("fabric:/MyApp/MyService");
        var endpointAddress = "http://localhost:8080/";

        var appWrapper = Substitute.For<IApplicationWrapper>();
        appWrapper.ApplicationName.Returns(appName);
        appWrapper.ApplicationTypeName.Returns("MyAppType");
        appWrapper.ApplicationStatus.Returns(ApplicationStatus.Ready);

        var serviceWrapper = Substitute.For<IServiceWrapper>();
        serviceWrapper.ServiceName.Returns(serviceName);
        serviceWrapper.ServiceTypeName.Returns("MyServiceTypeAPI"); // Matches API heuristic
        serviceWrapper.ServiceKind.Returns(ServiceKind.Stateless);

        var partitionWrapper = Substitute.For<IResolvedServicePartitionWrapper>();
        var endpointWrapper = Substitute.For<IResolvedServiceEndpointWrapper>();
        endpointWrapper.Address.Returns(endpointAddress);
        partitionWrapper.GetEndpoint().Returns(endpointWrapper);

        // Cache misses initially
        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(null));
        _mockCache.GetPartitionAsync(Arg.Any<string>()).Returns(Task.FromResult<IResolvedServicePartitionWrapper?>(null));

        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { appWrapper }));
        _mockClient.GetServiceListAsync(appName).Returns(Task.FromResult<IEnumerable<IServiceWrapper>>(new[] { serviceWrapper }));
        _mockClient.ResolveServicePartitionAsync(serviceName).Returns(Task.FromResult(partitionWrapper));

        // Act
        var result = await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());

        // Assert
        Assert.Equal(endpointAddress, result);
        await _mockCache.Received(1).SetApplicationAsync(Arg.Any<string>(), appWrapper);
        await _mockCache.Received(1).SetServiceAsync(Arg.Any<string>(), serviceWrapper);
        await _mockCache.Received(1).SetPartitionAsync(Arg.Any<string>(), partitionWrapper);
    }

    [Fact]
    public async Task ResolveUrlAsync_ThrowsKeyNotFound_WhenAppDoesNotExist()
    {
        // Arrange
        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(Enumerable.Empty<IApplicationWrapper>()));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _resolver.ResolveUrlAsync("NonExistentApp", Guid.NewGuid()));
    }

    [Fact]
    public async Task ResolveUrlAsync_ThrowsKeyNotFound_WhenServiceDoesNotExist()
    {
        // Arrange
        var appName = new Uri("fabric:/MyApp");
        var appWrapper = Substitute.For<IApplicationWrapper>();
        appWrapper.ApplicationName.Returns(appName);
        appWrapper.ApplicationTypeName.Returns("MyAppType");

        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(null));

        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { appWrapper }));
        _mockClient.GetServiceListAsync(appName).Returns(Task.FromResult<IEnumerable<IServiceWrapper>>(Enumerable.Empty<IServiceWrapper>()));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid()));
    }

    [Fact]
    public async Task ResolveFunctionUriAsync_ReturnsCorrectEndpoint_WhenFunctionServiceExists()
    {
        // Arrange
        var appName = new Uri("fabric:/MyFuncApp");
        var serviceName = new Uri("fabric:/MyFuncApp/MyFunction");
        var endpointAddress = "http://localhost:8081/";

        var appWrapper = Substitute.For<IApplicationWrapper>();
        appWrapper.ApplicationName.Returns(appName);
        appWrapper.ApplicationTypeName.Returns("MyFuncAppType");

        var serviceWrapper = Substitute.For<IServiceWrapper>();
        serviceWrapper.ServiceName.Returns(serviceName);
        serviceWrapper.ServiceTypeName.Returns("MyFunctionTypeFUNCTION"); // Matches FUNCTION heuristic
        serviceWrapper.ServiceKind.Returns(ServiceKind.Stateless);

        var partitionWrapper = Substitute.For<IResolvedServicePartitionWrapper>();
        var endpointWrapper = Substitute.For<IResolvedServiceEndpointWrapper>();
        endpointWrapper.Address.Returns(endpointAddress);
        partitionWrapper.GetEndpoint().Returns(endpointWrapper);

        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(null));
        _mockCache.GetPartitionAsync(Arg.Any<string>()).Returns(Task.FromResult<IResolvedServicePartitionWrapper?>(null));

        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { appWrapper }));
        _mockClient.GetServiceListAsync(appName).Returns(Task.FromResult<IEnumerable<IServiceWrapper>>(new[] { serviceWrapper }));
        _mockClient.ResolveServicePartitionAsync(serviceName).Returns(Task.FromResult(partitionWrapper));

        // Act
        var result = await _resolver.ResolveFunctionUriAsync("MyFuncApp", Guid.NewGuid());

        // Assert
        Assert.Equal(endpointAddress, result);
    }

    [Fact]
    public async Task ResolveUrlAsync_UsesCache_OnSecondCall()
    {
        // Arrange
        var appName = new Uri("fabric:/MyApp");
        var serviceName = new Uri("fabric:/MyApp/MyService");
        var endpointAddress = "http://localhost:8080/";

        var appWrapper = Substitute.For<IApplicationWrapper>();
        appWrapper.ApplicationName.Returns(appName);
        appWrapper.ApplicationTypeName.Returns("MyAppType");

        var serviceWrapper = Substitute.For<IServiceWrapper>();
        serviceWrapper.ServiceName.Returns(serviceName);
        serviceWrapper.ServiceTypeName.Returns("MyServiceTypeAPI");
        serviceWrapper.ServiceKind.Returns(ServiceKind.Stateless);

        var partitionWrapper = Substitute.For<IResolvedServicePartitionWrapper>();
        var endpointWrapper = Substitute.For<IResolvedServiceEndpointWrapper>();
        endpointWrapper.Address.Returns(endpointAddress);
        partitionWrapper.GetEndpoint().Returns(endpointWrapper);

        // First call: Cache miss
        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(null));
        _mockCache.GetPartitionAsync(Arg.Any<string>()).Returns(Task.FromResult<IResolvedServicePartitionWrapper?>(null));

        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { appWrapper }));
        _mockClient.GetServiceListAsync(appName).Returns(Task.FromResult<IEnumerable<IServiceWrapper>>(new[] { serviceWrapper }));
        _mockClient.ResolveServicePartitionAsync(serviceName).Returns(Task.FromResult(partitionWrapper));

        // Act 1
        await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());

        // Assert 1
        await _mockClient.Received(1).GetApplicationListAsync();
        await _mockClient.Received(1).GetServiceListAsync(appName);

        // Second call: Cache hit (simulate cache returning values)
        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(appWrapper));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(serviceWrapper));
        _mockCache.GetPartitionAsync(Arg.Any<string>()).Returns(Task.FromResult<IResolvedServicePartitionWrapper?>(partitionWrapper));

        _mockClient.ClearReceivedCalls(); // Clear previous calls to ensure we check new calls

        // Act 2
        await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());

        // Assert 2
        // Should NOT call SF client again
        await _mockClient.DidNotReceive().GetApplicationListAsync();
        await _mockClient.DidNotReceive().GetServiceListAsync(Arg.Any<Uri>());
    }

    [Fact]
    public async Task ResolveUrlAsync_RefreshesCache_WhenRequested()
    {
        // Arrange
        var appName = new Uri("fabric:/MyApp");
        var serviceName = new Uri("fabric:/MyApp/MyService");
        var endpointAddress = "http://localhost:8080/";

        var appWrapper = Substitute.For<IApplicationWrapper>();
        appWrapper.ApplicationName.Returns(appName);
        appWrapper.ApplicationTypeName.Returns("MyAppType");

        var serviceWrapper = Substitute.For<IServiceWrapper>();
        serviceWrapper.ServiceName.Returns(serviceName);
        serviceWrapper.ServiceTypeName.Returns("MyServiceTypeAPI");
        serviceWrapper.ServiceKind.Returns(ServiceKind.Stateless);

        var partitionWrapper = Substitute.For<IResolvedServicePartitionWrapper>();
        var endpointWrapper = Substitute.For<IResolvedServiceEndpointWrapper>();
        endpointWrapper.Address.Returns(endpointAddress);
        partitionWrapper.GetEndpoint().Returns(endpointWrapper);

        // First call: Cache miss
        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(null));
        _mockCache.GetPartitionAsync(Arg.Any<string>()).Returns(Task.FromResult<IResolvedServicePartitionWrapper?>(null));

        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { appWrapper }));
        _mockClient.GetServiceListAsync(appName).Returns(Task.FromResult<IEnumerable<IServiceWrapper>>(new[] { serviceWrapper }));
        _mockClient.ResolveServicePartitionAsync(serviceName).Returns(Task.FromResult(partitionWrapper));

        // Act 1
        await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());

        // Act 2: Refresh cache
        // Even if cache returns value, we should query cluster
        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(appWrapper));
        
        await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid(), null, refreshCache: true);

        // Assert
        // GetApplicationListAsync should be called twice (once for first call, once for refresh)
        await _mockClient.Received(2).GetApplicationListAsync();
    }
    [Fact]
    public void Dispose_UnregistersEvent()
    {
        // Act
        _resolver.Dispose();

        // Assert
        _mockFactory.Create().Received(1).ServiceNotificationFilterMatched -= Arg.Any<EventHandler<EventArgs>>();
    }

    [Fact]
    public void ServiceNotificationFilterMatched_ClearsCache()
    {
        // Arrange
        // We need to trigger the event. Since we mocked the client, we can't easily raise the event on the mock 
        // unless we setup the event subscription to capture the handler.
        // Alternatively, we can check if the handler was registered and invoke it reflecting or if NSubstitute supports raising events.
        // NSubstitute supports raising events: _mockClient.ServiceNotificationFilterMatched += Raise.Event<EventHandler>(this, EventArgs.Empty);
        
        // Act
        _mockClient.ServiceNotificationFilterMatched += Raise.Event<EventHandler<EventArgs>>(this, EventArgs.Empty);

        // Assert
        _mockCache.Received(1).ClearServiceAndPartitionCache();
    }

    [Fact]
    public async Task ResolveUrlAsync_Retries_OnTransientError()
    {
        // Arrange
        var appName = new Uri("fabric:/MyApp");
        var serviceName = new Uri("fabric:/MyApp/MyService");
        
        var appWrapper = Substitute.For<IApplicationWrapper>();
        appWrapper.ApplicationName.Returns(appName);
        appWrapper.ApplicationTypeName.Returns("MyAppType");

        var serviceWrapper = Substitute.For<IServiceWrapper>();
        serviceWrapper.ServiceName.Returns(serviceName);
        serviceWrapper.ServiceTypeName.Returns("MyServiceTypeAPI");
        serviceWrapper.ServiceKind.Returns(ServiceKind.Stateless);

        _mockCache.GetApplicationAsync(Arg.Any<string>()).Returns(Task.FromResult<IApplicationWrapper?>(null));
        _mockCache.GetServiceAsync(Arg.Any<string>()).Returns(Task.FromResult<IServiceWrapper?>(null));
        _mockCache.GetPartitionAsync(Arg.Any<string>()).Returns(Task.FromResult<IResolvedServicePartitionWrapper?>(null));

        _mockClient.GetApplicationListAsync().Returns(Task.FromResult<IEnumerable<IApplicationWrapper>>(new[] { appWrapper }));
        _mockClient.GetServiceListAsync(appName).Returns(Task.FromResult<IEnumerable<IServiceWrapper>>(new[] { serviceWrapper }));

        var partition = Substitute.For<IResolvedServicePartitionWrapper>();
        var endpoint = Substitute.For<IResolvedServiceEndpointWrapper>();
        endpoint.Address.Returns("http://localhost:8080/");
        partition.GetEndpoint().Returns(endpoint);

        var retryPolicy = Policy.Handle<FabricTransientException>().RetryAsync(1);
        _mockResilience.GetServiceFabricPolicy().Returns(retryPolicy);

        // Simulate transient failure then success
        int callCount = 0;
        _mockClient.ResolveServicePartitionAsync(serviceName).Returns(x => 
        {
            callCount++;
            if (callCount == 1) throw new FabricTransientException("Transient error");
            return Task.FromResult(partition);
        });

        var result = await _resolver.ResolveUrlAsync("MyApp", Guid.NewGuid());

        // Assert
        Assert.Equal("http://localhost:8080/", result);
        // Verify ResolveServicePartitionAsync was called twice
        await _mockClient.Received(2).ResolveServicePartitionAsync(serviceName);
    }
}
