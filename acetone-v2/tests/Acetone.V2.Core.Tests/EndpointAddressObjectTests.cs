using System.Text.Json;
using Acetone.V2.Core.ServiceFabric;
using Xunit;

namespace Acetone.V2.Core.Tests;

public class EndpointAddressObjectTests
{
    [Fact]
    public void CanDeserialize_ServiceFabricEndpointJson()
    {
        string json = "{\"Endpoints\":{\"Listener1\":\"https://localhost:8081/\"}}";
        
        var obj = JsonSerializer.Deserialize<EndpointAddressObject>(json);
        
        Assert.NotNull(obj);
        Assert.NotNull(obj.Endpoints);
        Assert.Single(obj.Endpoints);
        Assert.Equal("https://localhost:8081/", obj.Endpoints["Listener1"]);
    }

    [Fact]
    public void CanDeserialize_EmptyEndpoints()
    {
        string json = "{\"Endpoints\":{}}";
        
        var obj = JsonSerializer.Deserialize<EndpointAddressObject>(json);
        
        Assert.NotNull(obj);
        Assert.NotNull(obj.Endpoints);
        Assert.Empty(obj.Endpoints);
    }
}
