using System.Net;
using Acetone.V2.Core.ServiceFabric;
using NSubstitute;
using Xunit;

namespace Acetone.V2.Proxy.Tests.Integration;

public class ProxyIntegrationTests : IClassFixture<AcetoneProxyApplicationFactory>
{
    private readonly AcetoneProxyApplicationFactory _factory;

    public ProxyIntegrationTests(AcetoneProxyApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_UnknownHost_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Setup mock to return not found
        _factory.MockResolver.ResolveUrlAsync(Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(Task.FromException<string>(new KeyNotFoundException()));

        // Act
        var response = await client.GetAsync("http://unknown.example.com/api/values");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    
    [Fact]
    public async Task Get_KnownHost_Returns502_WhenDestinationUnreachable()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Setup mock to return a valid but unreachable URL
        // Since we don't have a real backend, YARP will try to forward and fail, returning 502 Bad Gateway.
        _factory.MockResolver.ResolveUrlAsync("myapp", Arg.Any<Guid>())
            .Returns(Task.FromResult("http://localhost:9999/")); // Unreachable port

        // Act
        var response = await client.GetAsync("http://myapp.example.com/api/values");

        // Assert
        // YARP returns 502 if it cannot connect to the destination
        // Assert
        // YARP returns 502 if it cannot connect to the destination
        if (response.StatusCode != HttpStatusCode.BadGateway)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Unexpected status code: {response.StatusCode}. Content: {content}");
        }
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
