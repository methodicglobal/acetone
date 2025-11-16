namespace Acetone.V2.Core;

/// <summary>
/// Specifies where in the URL the Service Fabric application name is located.
/// </summary>
public enum ApplicationNameLocation
{
    /// <summary>
    /// Application name is the first part of the subdomain.
    /// Example: service.uat.company.com → "service"
    /// </summary>
    Subdomain,

    /// <summary>
    /// Application name is the last part after hyphens in the subdomain.
    /// Example: uat-01-service.company.com → "service"
    /// </summary>
    SubdomainPostHyphens,

    /// <summary>
    /// Application name is the first part before hyphens in the subdomain.
    /// Example: service-uat-01.company.com → "service"
    /// </summary>
    SubdomainPreHyphens,

    /// <summary>
    /// Application name is the first URL path segment.
    /// Example: connect.uat.company.com/service → "service"
    /// </summary>
    FirstUrlFragment
}
