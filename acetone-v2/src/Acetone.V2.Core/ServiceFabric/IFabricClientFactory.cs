namespace Acetone.V2.Core.ServiceFabric;

public interface IFabricClientFactory
{
    IFabricClientWrapper Create();
}
