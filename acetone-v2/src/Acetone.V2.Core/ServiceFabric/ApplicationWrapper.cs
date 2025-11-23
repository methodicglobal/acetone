using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public class ApplicationWrapper : IApplicationWrapper
{
    private readonly Application _application;

    public ApplicationWrapper(Application application)
    {
        _application = application;
    }

    public Uri ApplicationName => _application.ApplicationName;
    public string ApplicationTypeName => _application.ApplicationTypeName;
    public string ApplicationTypeVersion => _application.ApplicationTypeVersion;
    public ApplicationStatus ApplicationStatus => _application.ApplicationStatus;
}
