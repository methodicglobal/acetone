namespace Acetone.V2.Core.Configuration;

/// <summary>
/// Specifies the type of credentials used for Service Fabric authentication.
/// </summary>
public enum CredentialsType
{
    /// <summary>
    /// Use local credentials (no authentication).
    /// Suitable for local development or unsecured clusters.
    /// </summary>
    Local,

    /// <summary>
    /// Use X.509 certificate authentication with thumbprint.
    /// The certificate is identified by its SHA-1 thumbprint.
    /// </summary>
    CertificateThumbprint,

    /// <summary>
    /// Use X.509 certificate authentication with common name.
    /// The certificate is identified by its subject common name.
    /// </summary>
    CertificateCommonName
}
