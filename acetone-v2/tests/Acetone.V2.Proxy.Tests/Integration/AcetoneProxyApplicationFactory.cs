using Acetone.V2.Core.Caching;
using Acetone.V2.Core.ServiceFabric;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Yarp.ReverseProxy.Forwarder;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Net;

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
            services.RemoveAll<IForwarderHttpClientFactory>();

            services.AddSingleton(MockResolver);
            services.AddSingleton(MockCache);
            services.AddSingleton<IForwarderHttpClientFactory, DangerousForwarderHttpClientFactory>();
        });
    }

    private class DangerousForwarderHttpClientFactory : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
                AutomaticDecompression = DecompressionMethods.All
            };
            return new HttpMessageInvoker(handler, disposeHandler: true);
        }
    }
}
