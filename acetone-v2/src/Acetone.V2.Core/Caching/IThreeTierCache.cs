using Acetone.V2.Core.ServiceFabric;

namespace Acetone.V2.Core.Caching;

public interface IThreeTierCache
{
    Task<IApplicationWrapper?> GetApplicationAsync(string key);
    Task SetApplicationAsync(string key, IApplicationWrapper application);
    
    Task<IServiceWrapper?> GetServiceAsync(string key);
    Task SetServiceAsync(string key, IServiceWrapper service);
    
    Task<IResolvedServicePartitionWrapper?> GetPartitionAsync(string key);
    Task SetPartitionAsync(string key, IResolvedServicePartitionWrapper partition);
    
    void ClearServiceAndPartitionCache();
    void ClearAll();
}
