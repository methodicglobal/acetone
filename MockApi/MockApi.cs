using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Data;

namespace MockApi
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal sealed class MockApi : StatelessService
    {
        public MockApi(StatelessServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        var builder = WebApplication.CreateBuilder();

                        builder.Services.AddSingleton<StatelessServiceContext>(serviceContext);
                        builder.WebHost
                                    .UseKestrel(opt =>
                                    {
                                        int port = serviceContext.CodePackageActivationContext.GetEndpoint("ServiceEndpoint").Port;
                                        opt.Listen(IPAddress.IPv6Any, port, listenOptions =>
                                        {
                                            var cert = GetCertificateFromStore();
                                            if (cert != null)
                                            {
                                                listenOptions.UseHttps(cert);
                                            }
                                            else
                                            {
                                                ServiceEventSource.Current.ServiceMessage(serviceContext, $"No certificate available, running HTTP on port {port}");
                                            }
                                        });
                                    })
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseUrls(url);
                        builder.Services.AddControllers();
                        builder.Services.AddOpenApi();
                        var app = builder.Build();
                        if (app.Environment.IsDevelopment())
                        {
                            app.MapOpenApi();
                        }
                        app.UseAuthorization();
                        app.MapControllers();

                        return app;

                    }))
            };
        }

        /// <summary>
        /// Attempts to locate a certificate for HTTPS hosting.
        /// Development: returns ASP.NET Core dev cert.
        /// Non-development: tries thumbprint (ACETONE_CERT_THUMBPRINT) then subject (ACETONE_CERT_SUBJECT). Returns null if none found.
        /// </summary>
        private static X509Certificate2? GetCertificateFromStore()
        {
            try
            {
                string? env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    // Match original logic: dev cert by OID + issuer CN=localhost (standard ASP.NET dev cert pattern)
                    const string aspNetHttpsOid = "1.3.6.1.4.1.311.84.1.1";
                    const string CNName = "CN=localhost";
                    var cert = FindFirst(certCollection =>
                    {
                        var byOid = certCollection.Find(X509FindType.FindByExtension, aspNetHttpsOid, validOnly: false);
                        return byOid.Find(X509FindType.FindByIssuerDistinguishedName, CNName, false);
                    });
                    if (cert != null) return cert;
                }

                // Non-dev (or dev fallback): check explicit thumbprint
                string? thumbprint = Environment.GetEnvironmentVariable("ACETONE_CERT_THUMBPRINT");
                if (!string.IsNullOrWhiteSpace(thumbprint))
                {
                    var cert = FindCertificateByThumbprint(thumbprint);
                    if (cert != null) return cert;
                }

                // Subject search
                string? subject = Environment.GetEnvironmentVariable("ACETONE_CERT_SUBJECT");
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    var cert = FindCertificateBySubject(subject);
                    if (cert != null) return cert;
                }
            }
            catch
            {
                // Swallow � fallback to HTTP
            }
            return null; // Will run HTTP only
        }

        private static X509Certificate2? FindCertificateByThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint)) return null;
            string normalised = thumbprint.Replace(" ", string.Empty).ToUpperInvariant();
            X509Certificate2? match = null;
            foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                try
                {
                    using (var store = new X509Store(StoreName.My, location))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        foreach (var cert in store.Certificates)
                        {
                            if (string.Equals(cert.Thumbprint?.Replace(" ", string.Empty), normalised, StringComparison.OrdinalIgnoreCase))
                            {
                                if (match == null || cert.NotBefore > match.NotBefore) match = cert;
                            }
                        }
                    }
                }
                catch { }
            }
            return match;
        }

        private static X509Certificate2? FindCertificateBySubject(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return null;
            X509Certificate2? match = null;
            foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                try
                {
                    using (var store = new X509Store(StoreName.My, location))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        foreach (var cert in store.Certificates)
                        {
                            var certSubject = cert.Subject;
                            if (!string.IsNullOrEmpty(certSubject) && certSubject.IndexOf(subject, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (match == null || cert.NotBefore > match.NotBefore) match = cert;
                            }
                        }
                    }
                }
                catch { }
            }
            return match;
        }

        private static X509Certificate2? FindFirst(Func<X509Certificate2Collection, X509Certificate2Collection?> selector)
        {
            foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                try
                {
                    using (var store = new X509Store(StoreName.My, location))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        var result = selector(store.Certificates);
                        if (result != null && result.Count > 0) return result[0];
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
