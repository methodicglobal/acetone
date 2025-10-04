using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Methodic.Acetone.Tests
{
	public class TestableServiceFabricUrlResolver : ServiceFabricUrlResolver
	{
		private readonly bool useMockData;
		// Changed to thread-safe concurrent dictionary; lazy population to satisfy concurrency test expectations
		private readonly ConcurrentDictionary<string, string> mockEndpoints = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public int MockCachedApplicationCount => mockEndpoints.Count;

		private static readonly Regex ServicePattern = new Regex(@"^Service([A-H])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public TestableServiceFabricUrlResolver(ILogger logger, string clusterConnectionString, bool useMockData = true)
			: base(logger, clusterConnectionString)
		{
			this.useMockData = useMockData;
			// Previous eager initialisation removed. Cache now starts empty and is filled on demand.
		}

		public static TestableServiceFabricUrlResolver Create(ILogger logger, string clusterConnectionString, bool useMockData = true)
			=> new TestableServiceFabricUrlResolver(logger, clusterConnectionString, useMockData);

		private string TryMaterializeEndpoint(string applicationName)
		{
			if (string.IsNullOrWhiteSpace(applicationName))
			{
				throw new ArgumentException("Application name required", nameof(applicationName));
			}

			// Guard standard service
			if (applicationName.Equals("Guard", StringComparison.OrdinalIgnoreCase))
			{
				return "https://guard.pav.meth.wtf";
			}
			// Guard PR specific (kept for parity with other tests if needed)
			if (applicationName.Equals("Guard-PR12906", StringComparison.OrdinalIgnoreCase))
			{
				return "https://guard-12906.pav.meth.wtf";
			}

			// Generic ServiceX pattern (A-H)
			var match = ServicePattern.Match(applicationName);
			if (match.Success)
			{
				char svc = char.ToLowerInvariant(match.Groups[1].Value[0]);
				return $"https://service{svc}.pav.meth.wtf";
			}

			// If pattern not recognised we treat as non-existent mock application
			return null;
		}

		public new Task<string> ResolveServiceUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			if (!useMockData)
			{
				return base.ResolveServiceUri(applicationName, invocationId, version, refreshCache);
			}

			if (string.IsNullOrWhiteSpace(applicationName))
			{
				throw new ArgumentException("Application name required", nameof(applicationName));
			}

			// Lazily create the endpoint if it does not yet exist
			var endpoint = mockEndpoints.GetOrAdd(applicationName, name =>
			{
				var ep = TryMaterializeEndpoint(name);
				if (ep == null)
				{
					// Remove placeholder added by GetOrAdd (will be null) by throwing so test surfaces missing app
					throw new KeyNotFoundException($"No mock application {name}");
				}
				return ep;
			});

			return Task.FromResult(endpoint);
		}

		public new Task<string> ResolveFunctionUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
			=> ResolveServiceUri(applicationName, invocationId, version, refreshCache);
	}
}