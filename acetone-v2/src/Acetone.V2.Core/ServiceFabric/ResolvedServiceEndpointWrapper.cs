using System.Fabric;

namespace Acetone.V2.Core.ServiceFabric;

public class ResolvedServiceEndpointWrapper : IResolvedServiceEndpointWrapper
{
    private readonly ResolvedServiceEndpoint _endpoint;

    public ResolvedServiceEndpointWrapper(ResolvedServiceEndpoint endpoint)
    {
        _endpoint = endpoint;
    }

    public string Address => _endpoint.Address;
}
