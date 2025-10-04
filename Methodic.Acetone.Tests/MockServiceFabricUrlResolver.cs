using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Mock implementation of Service Fabric URL resolver for testing without a real cluster.
	/// This implementation simulates Service Fabric behavior using in-memory data structures.
	/// </summary>
	public class MockServiceFabricUrlResolver : IServiceUrlResolver, IDisposable
	{
		private readonly ILogger logger;
		private readonly string clusterEndpoint;
		private readonly Dictionary<string, MockServiceFabricApplication> applications;

		public MockServiceFabricUrlResolver(ILogger logger, string clusterEndpoint)
		{
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.clusterEndpoint = clusterEndpoint;
			this.applications = new Dictionary<string, MockServiceFabricApplication>(StringComparer.OrdinalIgnoreCase);
			InitializeMockData();
		}

		/// <summary>
		/// Adds a mock application to the resolver for testing purposes.
		/// </summary>
		public void AddMockApplication(string applicationName, string endpoint, bool isPullRequest = false)
		{
			if (string.IsNullOrWhiteSpace(applicationName))
			{
				throw new ArgumentException("Application name cannot be null or empty", nameof(applicationName));
			}

			if (string.IsNullOrWhiteSpace(endpoint))
			{
				throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint));
			}

			if (!ServiceFabricUrlParser.IsValidEndpoint(endpoint))
			{
				throw new ArgumentException($"Invalid endpoint format: {endpoint}", nameof(endpoint));
			}

			applications[applicationName] = new MockServiceFabricApplication
			{
				ApplicationName = applicationName,
				Endpoint = endpoint,
				IsPullRequest = isPullRequest
			};

			logger.WriteEntry($"[MOCK] Added application: {applicationName} -> {endpoint}", LogEntryType.Informational);
		}

		/// <summary>
		/// Removes a mock application from the resolver.
		/// </summary>
		public bool RemoveMockApplication(string applicationName)
		{
			bool removed = applications.Remove(applicationName);
			if (removed)
			{
				logger.WriteEntry($"[MOCK] Removed application: {applicationName}", LogEntryType.Informational);
			}
			return removed;
		}

		/// <summary>
		/// Gets all registered mock applications.
		/// </summary>
		public IReadOnlyDictionary<string, MockServiceFabricApplication> GetApplications()
		{
			return applications;
		}

		/// <summary>
		/// Clears all mock applications.
		/// </summary>
		public void ClearApplications()
		{
			applications.Clear();
			logger.WriteEntry("[MOCK] Cleared all applications", LogEntryType.Informational);
		}

		public Task<string> ResolveServiceUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			return ResolveServiceUriInternal(applicationName, invocationId);
		}

		public Task<string> ResolveServiceUri(string applicationName, Guid invocationId, string version, bool refreshCache, string serviceTypeFilter)
		{
			// Remove overload (interface reverted) - keep for backward compatibility but delegate
			return ResolveServiceUriInternal(applicationName, invocationId);
		}

		private async Task<string> ResolveServiceUriInternal(string applicationName, Guid invocationId)
		{
			logger.WriteEntry($"[MOCK] Resolving service URI for application: {applicationName}, invocationId: {invocationId}", LogEntryType.Informational);
			await Task.Delay(1);
			if (string.IsNullOrWhiteSpace(applicationName)) throw new ArgumentException("Application name cannot be null or empty", nameof(applicationName));
			if (!applications.TryGetValue(applicationName, out var app))
			{
				string error = $"No matching application with name '{applicationName}' could be found on cluster {clusterEndpoint}. Available applications: {string.Join(", ", applications.Keys)}";
				logger.WriteEntry(error, LogEntryType.Error);
				throw new KeyNotFoundException(error);
			}
			return app.Endpoint;
		}

		public Task<string> ResolveFunctionUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			return ResolveServiceUri(applicationName, invocationId, version, refreshCache);
		}

		private void InitializeMockData()
		{
			logger.WriteEntry("[MOCK] Initializing mock Service Fabric data", LogEntryType.Informational);

			// Create regular service applications (ServiceA through ServiceH)
			for (char service = 'A'; service <= 'H'; service++)
			{
				string serviceName = $"Service{service}";
				string endpoint = $"https://service{service.ToString().ToLower()}.pav.meth.wtf";
				AddMockApplication(serviceName, endpoint, isPullRequest: false);
			}

			// Create pull request variants for each service
			var prNumbers = new[] { "1234", "5678", "9999", "12906" };
			for (char service = 'A'; service <= 'H'; service++)
			{
				string serviceName = $"Service{service}";
				foreach (string prNumber in prNumbers)
				{
					string prAppName = $"{serviceName}-PR{prNumber}";
					string prEndpoint = $"https://service{service.ToString().ToLower()}-{prNumber}.pav.meth.wtf";
					AddMockApplication(prAppName, prEndpoint, isPullRequest: true);
				}
			}

			// Add some additional realistic services
			AddMockApplication("Guard", "https://guard.pav.meth.wtf", isPullRequest: false);
			AddMockApplication("Guard-PR12906", "https://guard-12906.pav.meth.wtf", isPullRequest: true);
			AddMockApplication("Api", "https://api.methodic.com", isPullRequest: false);
			AddMockApplication("Api-PR1234", "https://api-1234.test.methodic.com", isPullRequest: true);

			logger.WriteEntry($"[MOCK] Initialized {applications.Count} mock applications", LogEntryType.Informational);
		}

		public void Dispose()
		{
			// nothing to dispose currently
		}
	}

	/// <summary>
	/// Represents a mock Service Fabric application for testing.
	/// </summary>
	public class MockServiceFabricApplication
	{
		public string ApplicationName { get; set; }
		public string Endpoint { get; set; }
		public bool IsPullRequest { get; set; }
		public string ApplicationTypeName => ApplicationName + "Type";
		public string ApplicationTypeVersion => "1.0.0";

		public override string ToString()
		{
			return $"{ApplicationName} -> {Endpoint} (PR: {IsPullRequest})";
		}
	}
}
