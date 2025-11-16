using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Acetone.V2.Core;

namespace Acetone.V2.PerformanceTests;

/// <summary>
/// Performance benchmarks for URL parsing operations.
/// Run with: dotnet run -c Release
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class UrlParsingBenchmarks
{
    private const string SubdomainUrl = "https://myservice.company.com/api/endpoint?query=value";
    private const string PullRequestUrl = "https://service-12345.company.com/api/test";
    private const string FirstFragmentUrl = "https://api.company.com/myservice/endpoint";
    private const string Ipv6Url = "https://[2001:db8::1]:8080/api/test";

    private const string ServiceFabricEndpointJson = @"{""Endpoints"":{"""":""https://node-01.cluster.com:5555""}}";
    private const string PlainEndpoint = "https://node-01.cluster.com:5555";

    [Benchmark(Description = "Parse Subdomain URL")]
    public void ParseSubdomainUrl()
    {
        ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
            SubdomainUrl,
            ApplicationNameLocation.Subdomain,
            out string? appName);
    }

    [Benchmark(Description = "Parse Pull Request URL")]
    public void ParsePullRequestUrl()
    {
        ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
            PullRequestUrl,
            ApplicationNameLocation.Subdomain,
            out string? appName);
    }

    [Benchmark(Description = "Parse FirstUrlFragment URL")]
    public void ParseFirstFragmentUrl()
    {
        ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
            FirstFragmentUrl,
            ApplicationNameLocation.FirstUrlFragment,
            out string? appName);
    }

    [Benchmark(Description = "Parse IPv6 URL")]
    public void ParseIpv6Url()
    {
        ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
            Ipv6Url,
            ApplicationNameLocation.Subdomain,
            out string? appName);
    }

    [Benchmark(Description = "Extract endpoint from JSON")]
    public void ExtractEndpointFromJson()
    {
        try
        {
            ServiceFabricUrlParser.EndpointJsonToText(ServiceFabricEndpointJson, null);
        }
        catch
        {
            // Ignore exceptions for benchmark
        }
    }

    [Benchmark(Description = "Extract endpoint from plain URL")]
    public void ExtractEndpointFromPlainUrl()
    {
        try
        {
            ServiceFabricUrlParser.EndpointJsonToText(PlainEndpoint, null);
        }
        catch
        {
            // Ignore exceptions for benchmark
        }
    }

    [Benchmark(Description = "Validate endpoint URL")]
    public void ValidateEndpoint()
    {
        ServiceFabricUrlParser.IsValidEndpoint(PlainEndpoint);
    }

    [Benchmark(Description = "Normalize local endpoint")]
    public void NormalizeLocalEndpoint()
    {
        ServiceFabricUrlParser.NormalizeLocalEndpoint("https://0.0.0.0:8080/api");
    }
}

/// <summary>
/// Entry point for running benchmarks.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<UrlParsingBenchmarks>();
    }
}
