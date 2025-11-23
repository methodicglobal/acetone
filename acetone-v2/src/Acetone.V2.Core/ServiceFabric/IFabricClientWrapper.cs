using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public interface IFabricClientWrapper : IDisposable
{
    Task<IEnumerable<IApplicationTypeWrapper>> GetApplicationTypeListAsync();
    Task<IEnumerable<IApplicationWrapper>> GetApplicationListAsync();
    Task<IEnumerable<IServiceWrapper>> GetServiceListAsync(Uri applicationName);
    Task<IResolvedServicePartitionWrapper> ResolveServicePartitionAsync(Uri serviceName);
    Task RegisterServiceNotificationFilterAsync(ServiceNotificationFilterDescription description);
    
    event EventHandler<EventArgs>? ServiceNotificationFilterMatched;
}
