using System.Fabric;

namespace Acetone.V2.Core.ServiceFabric;

public class ResolvedServicePartitionWrapper : IResolvedServicePartitionWrapper
{
    private readonly ResolvedServicePartition _partition;

    public ResolvedServicePartitionWrapper(ResolvedServicePartition partition)
    {
        _partition = partition;
    }

    public IResolvedServiceEndpointWrapper GetEndpoint()
    {
        return new ResolvedServiceEndpointWrapper(_partition.GetEndpoint());
    }
}
