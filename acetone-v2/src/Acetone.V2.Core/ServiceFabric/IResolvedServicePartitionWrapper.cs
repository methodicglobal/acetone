namespace Acetone.V2.Core.ServiceFabric;

public interface IResolvedServicePartitionWrapper
{
    IResolvedServiceEndpointWrapper GetEndpoint();
}
