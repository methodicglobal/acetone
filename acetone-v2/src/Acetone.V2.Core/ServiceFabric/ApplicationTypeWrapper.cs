using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public class ApplicationTypeWrapper : IApplicationTypeWrapper
{
    private readonly ApplicationType _applicationType;

    public ApplicationTypeWrapper(ApplicationType applicationType)
    {
        _applicationType = applicationType;
    }

    public string ApplicationTypeName => _applicationType.ApplicationTypeName;
}
