using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Acetone.V2.Core.Tests;

public class ConfigurationTests
{
    private readonly AcetoneOptionsValidator _validator = new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = new[] { "localhost:19000" },
            CredentialsType = CredentialsType.Local
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MissingConnectionStrings_ReturnsFailure()
    {
        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = Array.Empty<string>(),
            CredentialsType = CredentialsType.Local
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("At least one ClusterConnectionString must be provided.", result.FailureMessage);
    }

    [Fact]
    public void Validate_CertificateThumbprint_MissingThumbprint_ReturnsFailure()
    {
        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = new[] { "localhost:19000" },
            CredentialsType = CredentialsType.CertificateThumbprint,
            ClientCertificateThumbprint = null // Missing
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("ClientCertificateThumbprint is required when CredentialsType is CertificateThumbprint.", result.FailureMessage);
    }

    [Fact]
    public void Validate_CertificateCommonName_MissingSubjectDN_ReturnsFailure()
    {
        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = new[] { "localhost:19000" },
            CredentialsType = CredentialsType.CertificateCommonName,
            ClientCertificateSubjectDistinguishedName = null // Missing
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("ClientCertificateSubjectDistinguishedName is required when CredentialsType is CertificateCommonName.", result.FailureMessage);
    }

    [Fact]
    public void Validate_InvalidTimeout_ReturnsFailure()
    {
        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = new[] { "localhost:19000" },
            ConnectionTimeout = TimeSpan.FromSeconds(-1)
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("ConnectionTimeout must be positive.", result.FailureMessage);
    }
    [Fact]
    public void Validate_WindowsCredentials_ReturnsSuccess()
    {
        var options = new AcetoneOptions
        {
            ClusterConnectionStrings = new[] { "localhost:19000" },
            CredentialsType = CredentialsType.Windows
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }
}
