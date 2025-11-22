using Acetone.V2.Core.Caching;
using Acetone.V2.Core.ServiceFabric;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Acetone.V2.Proxy.Tests.Integration;

public class AcetoneProxyApplicationFactory : WebApplicationFactory<Program>
{
    public IServiceFabricResolver MockResolver { get; } = Substitute.For<IServiceFabricResolver>();
    public IThreeTierCache MockCache { get; } = Substitute.For<IThreeTierCache>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceFabricResolver>();
            services.RemoveAll<IThreeTierCache>();

            services.AddSingleton(MockResolver);
            services.AddSingleton(MockCache);
        });
    }
}
