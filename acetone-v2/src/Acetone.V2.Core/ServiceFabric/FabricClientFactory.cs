using System.Fabric;
using System.Security.Cryptography.X509Certificates;
using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acetone.V2.Core.ServiceFabric;

public class FabricClientFactory : IFabricClientFactory
{
    private readonly AcetoneOptions _options;
    private readonly ILogger<FabricClientFactory> _logger;

    public FabricClientFactory(IOptions<AcetoneOptions> options, ILogger<FabricClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IFabricClientWrapper Create()
    {
        var settings = new FabricClientSettings
        {
            ClientFriendlyName = "Acetone V2",
            ConnectionInitializationTimeout = _options.ConnectionTimeout,
            KeepAliveInterval = TimeSpan.FromSeconds(10),
            NotificationCacheUpdateTimeout = TimeSpan.FromSeconds(2),
            NotificationGatewayConnectionTimeout = TimeSpan.FromSeconds(2),
            PartitionLocationCacheLimit = _options.PartitionCacheLimit,
            PartitionLocationCacheBucketCount = 1024,
            ServiceChangePollInterval = TimeSpan.FromSeconds(5)
        };

        string[] endpoints = _options.ClusterConnectionStrings;

        if (_options.CredentialsType == CredentialsType.Local)
        {
            _logger.LogInformation("Initializing FabricClient with Local credentials");
            return new FabricClientWrapper(new FabricClient(settings, endpoints));
        }

        X509Credentials? credentials = null;

        if (_options.CredentialsType == CredentialsType.CertificateThumbprint)
        {
            _logger.LogInformation("Initializing FabricClient with CertificateThumbprint credentials");
            credentials = new X509Credentials
            {
                FindType = X509FindType.FindByThumbprint,
                FindValue = _options.ClientCertificateThumbprint,
                StoreLocation = StoreLocation.LocalMachine,
                StoreName = StoreName.My.ToString()
            };

            if (_options.ServerCertificateThumbprints != null)
            {
                foreach (var tp in _options.ServerCertificateThumbprints)
                {
                    credentials.RemoteCertThumbprints.Add(tp);
                }
            }
        }
        else if (_options.CredentialsType == CredentialsType.CertificateCommonName)
        {
            _logger.LogInformation("Initializing FabricClient with CertificateCommonName credentials");
            
            string? resolvedThumbprint = FindCertificateThumbprint(
                _options.ClientCertificateSubjectDistinguishedName!, 
                _options.ClientCertificateIssuerDistinguishedName!);

            if (resolvedThumbprint == null)
            {
                throw new ArgumentException($"Could not find client certificate with Subject '{_options.ClientCertificateSubjectDistinguishedName}' and Issuer '{_options.ClientCertificateIssuerDistinguishedName}'");
            }

            credentials = new X509Credentials
            {
                FindType = X509FindType.FindByThumbprint,
                FindValue = resolvedThumbprint,
                StoreLocation = StoreLocation.LocalMachine,
                StoreName = StoreName.My.ToString()
            };

            if (_options.ServerCertificateCommonNames != null)
            {
                foreach (var cn in _options.ServerCertificateCommonNames)
                {
                    credentials.RemoteCommonNames.Add(cn);
                }
            }
        }

        if (credentials == null)
        {
            throw new InvalidOperationException("Failed to create X509Credentials");
        }

        return new FabricClientWrapper(new FabricClient(credentials, settings, endpoints));
    }

    private string? FindCertificateThumbprint(string subjectName, string issuerName)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var issuerResults = store.Certificates.Find(X509FindType.FindByIssuerDistinguishedName, issuerName, true);
        var subjectResults = issuerResults.Find(X509FindType.FindBySubjectDistinguishedName, subjectName, true);

        if (subjectResults.Count == 0)
        {
            _logger.LogError("No certificates found for Subject '{Subject}' and Issuer '{Issuer}'", subjectName, issuerName);
            return null;
        }

        var selectedCert = subjectResults.OfType<X509Certificate2>()
            .OrderByDescending(c => c.NotBefore)
            .First();

        _logger.LogInformation("Resolved certificate thumbprint {Thumbprint} for Subject '{Subject}'", selectedCert.Thumbprint, subjectName);
        return selectedCert.Thumbprint;
    }
}
