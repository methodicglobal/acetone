using Microsoft.Extensions.Options;

namespace Acetone.V2.Core.Configuration;

public class AcetoneOptionsValidator : IValidateOptions<AcetoneOptions>
{
    public ValidateOptionsResult Validate(string? name, AcetoneOptions options)
    {
        var errors = new List<string>();

        if (options.ClusterConnectionStrings == null || options.ClusterConnectionStrings.Length == 0)
        {
            errors.Add("At least one ClusterConnectionString must be provided.");
        }

        if (options.CredentialsType == CredentialsType.CertificateThumbprint)
        {
            if (string.IsNullOrWhiteSpace(options.ClientCertificateThumbprint))
            {
                errors.Add("ClientCertificateThumbprint is required when CredentialsType is CertificateThumbprint.");
            }
        }

        if (options.CredentialsType == CredentialsType.CertificateCommonName)
        {
            if (string.IsNullOrWhiteSpace(options.ClientCertificateSubjectDistinguishedName))
            {
                errors.Add("ClientCertificateSubjectDistinguishedName is required when CredentialsType is CertificateCommonName.");
            }
        }

        if (options.ConnectionTimeout.TotalMilliseconds <= 0)
        {
            errors.Add("ConnectionTimeout must be positive.");
        }

        if (errors.Count > 0)
        {
            return ValidateOptionsResult.Fail(errors);
        }

        return ValidateOptionsResult.Success;
    }
}
