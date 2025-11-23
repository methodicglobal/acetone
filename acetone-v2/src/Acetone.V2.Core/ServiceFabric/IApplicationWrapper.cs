using System.Fabric.Query;

namespace Acetone.V2.Core.ServiceFabric;

public interface IApplicationWrapper
{
    Uri ApplicationName { get; }
    string ApplicationTypeName { get; }
    string ApplicationTypeVersion { get; }
    ApplicationStatus ApplicationStatus { get; }
}
