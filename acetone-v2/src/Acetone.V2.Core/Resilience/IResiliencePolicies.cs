using Polly;

namespace Acetone.V2.Core.Resilience;

public interface IResiliencePolicies
{
    IAsyncPolicy GetServiceFabricPolicy();
}
