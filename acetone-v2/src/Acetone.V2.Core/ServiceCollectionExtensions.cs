using Acetone.V2.Core.Caching;
using Acetone.V2.Core.Configuration;
using Acetone.V2.Core.Resilience;
using Acetone.V2.Core.ServiceFabric;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Acetone.V2.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcetone(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<AcetoneOptions>(configuration.GetSection(AcetoneOptions.SectionName));
        services.AddSingleton<IValidateOptions<AcetoneOptions>, AcetoneOptionsValidator>();

        // Caching
        services.AddMemoryCache(); // For Partition Cache
        services.AddSingleton<Acetone.V2.Core.Diagnostics.AcetoneTelemetry>();
        services.AddSingleton<IThreeTierCache, ThreeTierCache>();

        // Resilience
        services.AddSingleton<IResiliencePolicies, ResiliencePolicies>();

        // Service Fabric
        services.AddSingleton<IFabricClientFactory, FabricClientFactory>();
        services.AddSingleton<IServiceFabricResolver, ServiceFabricResolver>();

        return services;
    }
}
