using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public interface IServiceWrapper
{
    Uri ServiceName { get; }
    string ServiceTypeName { get; }
    ServiceKind ServiceKind { get; }
}
