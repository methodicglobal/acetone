namespace Acetone.V2.Core;

/// <summary>
/// Represents the endpoint address object returned by Service Fabric.
/// </summary>
public class EndpointAddressObject
{
    /// <summary>
    /// Dictionary of endpoint names to their URLs.
    /// The key is the endpoint name (can be empty string for unnamed endpoints).
    /// The value is the endpoint URL.
    /// </summary>
    public Dictionary<string, string> Endpoints { get; set; } = new();
}
