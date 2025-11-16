using Microsoft.Extensions.Logging;
using Acetone.V2.Core;

namespace Acetone.V2.Core.Tests;

public class ServiceFabricUrlParserTests
{
    private class TestLogger : ILogger<ServiceFabricUrlParser>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private readonly ILogger<ServiceFabricUrlParser> _logger;

    public ServiceFabricUrlParserTests()
    {
        _logger = new TestLogger();
    }

    #region TryGetApplicationNameFromUrl Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetApplicationNameFromUrl_NullOrWhitespace_ReturnsFalse(string? url)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url!, ApplicationNameLocation.Subdomain, out var appName);

        // Assert
        Assert.False(result);
        Assert.Null(appName);
    }

    [Theory]
    [InlineData("https://service.uat.company.com", ApplicationNameLocation.Subdomain, "service")]
    [InlineData("http://myapp.dev.example.org", ApplicationNameLocation.Subdomain, "myapp")]
    [InlineData("service.uat.company.com", ApplicationNameLocation.Subdomain, "service")] // Without protocol
    public void TryGetApplicationNameFromUrl_Subdomain_ExtractsCorrectly(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://service-uat-01.company.com", ApplicationNameLocation.SubdomainPreHyphens, "service")]
    [InlineData("http://app-dev-02.example.org", ApplicationNameLocation.SubdomainPreHyphens, "app")]
    [InlineData("myservice-prod-v1.domain.com", ApplicationNameLocation.SubdomainPreHyphens, "myservice")]
    public void TryGetApplicationNameFromUrl_SubdomainPreHyphens_ExtractsCorrectly(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://uat-01-service.company.com", ApplicationNameLocation.SubdomainPostHyphens, "service")]
    [InlineData("http://dev-02-myapp.example.org", ApplicationNameLocation.SubdomainPostHyphens, "myapp")]
    [InlineData("prod-v1-application.domain.com", ApplicationNameLocation.SubdomainPostHyphens, "application")]
    public void TryGetApplicationNameFromUrl_SubdomainPostHyphens_ExtractsCorrectly(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://connect.uat.company.com/service", ApplicationNameLocation.FirstUrlFragment, "service")]
    [InlineData("http://api.example.org/myapp/extra/path", ApplicationNameLocation.FirstUrlFragment, "myapp")]
    [InlineData("gateway.domain.com/application", ApplicationNameLocation.FirstUrlFragment, "application")]
    public void TryGetApplicationNameFromUrl_FirstUrlFragment_ExtractsCorrectly(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.Subdomain, "Guard-PR12906")]
    [InlineData("https://api.com/guard-12906", ApplicationNameLocation.FirstUrlFragment, "Guard-PR12906")]
    [InlineData("https://myservice-999.example.com", ApplicationNameLocation.Subdomain, "Myservice-PR999")]
    [InlineData("guard-12906.pav.meth.wtf", ApplicationNameLocation.Subdomain, "Guard-PR12906")] // Without protocol
    public void TryGetApplicationNameFromUrl_PullRequestPattern_TransformsCorrectly(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://guard.pav.meth.wtf", ApplicationNameLocation.Subdomain, "guard")]
    [InlineData("https://my-service.example.com", ApplicationNameLocation.Subdomain, "my-service")]
    [InlineData("https://api.com/guard", ApplicationNameLocation.FirstUrlFragment, "guard")]
    public void TryGetApplicationNameFromUrl_NonPullRequestPattern_ReturnsAsIs(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://[::1]:8080/service", ApplicationNameLocation.FirstUrlFragment, "service")]
    [InlineData("https://[2001:db8:85a3::8a2e:370:7334]:8080", ApplicationNameLocation.Subdomain, "[2001:db8:85a3::8a2e:370:7334]")]
    public void TryGetApplicationNameFromUrl_IPv6Addresses_HandlesCorrectly(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    [Theory]
    [InlineData("https://api.com/service?query=value", ApplicationNameLocation.FirstUrlFragment, "service")]
    [InlineData("https://service.com?foo=bar&baz=qux", ApplicationNameLocation.Subdomain, "service")]
    public void TryGetApplicationNameFromUrl_WithQueryStrings_IgnoresQueryString(string url, ApplicationNameLocation location, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, location, out var appName);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, appName);
    }

    #endregion

    #region EndpointJsonToText Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EndpointJsonToText_NullOrWhitespace_ThrowsArgumentNullException(string? json)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => ServiceFabricUrlParser.EndpointJsonToText(json!, _logger));
        Assert.Equal("json", ex.ParamName);
    }

    [Theory]
    [InlineData("https://dev-ws-01.methodic.online:5555", "https://dev-ws-01.methodic.online:5555")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("https://example.com/", "https://example.com")] // Trailing slash removed
    public void EndpointJsonToText_PlainUrl_ReturnsUrl(string json, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.EndpointJsonToText(json, _logger);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EndpointJsonToText_JsonWithUnnamedEndpoint_ExtractsUrl()
    {
        // Arrange
        var json = "{\"Endpoints\":{\"\":\"https://dev-ws-01.methodic.online:5555\"}}";

        // Act
        var result = ServiceFabricUrlParser.EndpointJsonToText(json, _logger);

        // Assert
        Assert.Equal("https://dev-ws-01.methodic.online:5555", result);
    }

    [Fact]
    public void EndpointJsonToText_JsonWithNamedEndpoint_ExtractsUrl()
    {
        // Arrange
        var json = "{\"Endpoints\":{\"HttpListener\":\"https://dev-ws-03.methodic.online:999/\"}}";

        // Act
        var result = ServiceFabricUrlParser.EndpointJsonToText(json, _logger);

        // Assert
        Assert.Equal("https://dev-ws-03.methodic.online:999", result);
    }

    [Fact]
    public void EndpointJsonToText_JsonWithEscapedSlashes_HandlesCorrectly()
    {
        // Arrange
        var json = "{\"Endpoints\":{\"\":\"https:\\/\\/dev-ws-04.methodic.online:8899\"}}";

        // Act
        var result = ServiceFabricUrlParser.EndpointJsonToText(json, _logger);

        // Assert
        Assert.Equal("https://dev-ws-04.methodic.online:8899", result);
    }

    [Theory]
    [InlineData("{\"Endpoints\":{\"\":\"https://[::1]:8080\"}}", "https://[::1]:8080")]
    [InlineData("{\"Endpoints\":{\"\":\"https://[2001:db8:85a3::8a2e:370:7334]:8080/\"}}", "https://[2001:db8:85a3::8a2e:370:7334]:8080")]
    [InlineData("https://[::1]:8080", "https://[::1]:8080")]
    public void EndpointJsonToText_IPv6Addresses_ExtractsCorrectly(string json, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.EndpointJsonToText(json, _logger);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("{\"Endpoints\":{}}")]
    [InlineData("{\"NoEndpoints\":\"value\"}")]
    [InlineData("invalid json")]
    [InlineData("tcp://remoting-endpoint:9999")]
    public void EndpointJsonToText_NoValidHttpEndpoint_ThrowsException(string json)
    {
        // Act & Assert
        var ex = Assert.Throws<Exception>(() => ServiceFabricUrlParser.EndpointJsonToText(json, _logger));
        Assert.StartsWith("Found no valid HTTP/HTTPS endpoint in:", ex.Message);
    }

    #endregion

    #region IsValidEndpoint Tests

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("https://[::1]:8080", true)]
    [InlineData("tcp://remoting:9999", false)]
    [InlineData("not-a-url", false)]
    [InlineData("ftp://example.com", false)]
    public void IsValidEndpoint_VariousInputs_ReturnsExpected(string? endpoint, bool expected)
    {
        // Act
        var result = ServiceFabricUrlParser.IsValidEndpoint(endpoint!);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region NormalizeLocalEndpoint Tests

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void NormalizeLocalEndpoint_NullOrWhitespace_ReturnsAsIs(string? endpoint, string? expected)
    {
        // Act
        var result = ServiceFabricUrlParser.NormalizeLocalEndpoint(endpoint!, _logger);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080", "http://127.0.0.1:8080")]
    [InlineData("https://0.0.0.0:5555/path", "https://127.0.0.1:5555/path")]
    public void NormalizeLocalEndpoint_ZeroIPv4_ReplacesWithLoopback(string endpoint, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.NormalizeLocalEndpoint(endpoint, _logger);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://[::]:8080", "http://[::1]:8080")]
    [InlineData("https://[::]:5555/path", "https://[::1]:5555/path")]
    public void NormalizeLocalEndpoint_ZeroIPv6_ReplacesWithLoopback(string endpoint, string expected)
    {
        // Act
        var result = ServiceFabricUrlParser.NormalizeLocalEndpoint(endpoint, _logger);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://example.com:8080")]
    [InlineData("http://localhost:5555")]
    [InlineData("https://[2001:db8::1]:8080")]
    public void NormalizeLocalEndpoint_RegularEndpoint_ReturnsUnchanged(string endpoint)
    {
        // Act
        var result = ServiceFabricUrlParser.NormalizeLocalEndpoint(endpoint, _logger);

        // Assert
        Assert.Equal(endpoint, result);
    }

    #endregion
}
