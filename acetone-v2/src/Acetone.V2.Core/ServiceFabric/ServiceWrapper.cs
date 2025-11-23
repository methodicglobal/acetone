using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public class ServiceWrapper : IServiceWrapper
{
    private readonly Service _service;

    public ServiceWrapper(Service service)
    {
        _service = service;
    }

    public Uri ServiceName => _service.ServiceName;
    public string ServiceTypeName => _service.ServiceTypeName;
    public ServiceKind ServiceKind => _service.ServiceKind;
}
