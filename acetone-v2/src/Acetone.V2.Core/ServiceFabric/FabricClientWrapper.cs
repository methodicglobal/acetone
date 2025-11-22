using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public class FabricClientWrapper : IFabricClientWrapper
{
    private readonly FabricClient _fabricClient;
    private bool _isDisposed;

    public event EventHandler<EventArgs>? ServiceNotificationFilterMatched;

    public FabricClientWrapper(FabricClient fabricClient)
    {
        _fabricClient = fabricClient;
        _fabricClient.ServiceManager.ServiceNotificationFilterMatched += OnServiceNotificationFilterMatched;
    }

    private void OnServiceNotificationFilterMatched(object? sender, EventArgs e)
    {
        ServiceNotificationFilterMatched?.Invoke(this, e);
    }

    public async Task<IEnumerable<IApplicationTypeWrapper>> GetApplicationTypeListAsync()
    {
        var list = await _fabricClient.QueryManager.GetApplicationTypeListAsync();
        return list.Select(at => new ApplicationTypeWrapper(at));
    }

    public async Task<IEnumerable<IApplicationWrapper>> GetApplicationListAsync()
    {
        var list = await _fabricClient.QueryManager.GetApplicationListAsync();
        return list.Select(a => new ApplicationWrapper(a));
    }

    public async Task<IEnumerable<IServiceWrapper>> GetServiceListAsync(Uri applicationName)
    {
        var list = await _fabricClient.QueryManager.GetServiceListAsync(applicationName);
        return list.Select(s => new ServiceWrapper(s));
    }

    public async Task<IResolvedServicePartitionWrapper> ResolveServicePartitionAsync(Uri serviceName)
    {
        var partition = await _fabricClient.ServiceManager.ResolveServicePartitionAsync(serviceName);
        return new ResolvedServicePartitionWrapper(partition);
    }

    public Task RegisterServiceNotificationFilterAsync(ServiceNotificationFilterDescription description)
    {
        return _fabricClient.ServiceManager.RegisterServiceNotificationFilterAsync(description);
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
            _fabricClient.ServiceManager.ServiceNotificationFilterMatched -= OnServiceNotificationFilterMatched;
            _fabricClient.Dispose();
        }
        _isDisposed = true;
    }
}
