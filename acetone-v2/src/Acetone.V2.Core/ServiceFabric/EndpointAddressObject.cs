using System.Text.Json.Serialization;

namespace Acetone.V2.Core.ServiceFabric;

public class EndpointAddressObject
{
    [JsonPropertyName("Endpoints")]
    public Dictionary<string, string> Endpoints { get; set; } = new();
}
