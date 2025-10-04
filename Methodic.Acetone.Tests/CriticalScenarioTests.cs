using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Tests for critical production scenarios that users are likely to encounter.
	/// These tests ensure Acetone works reliably across different deployment patterns.
	/// </summary>
	[TestClass]
	[TestCategory("Critical")]
	public class CriticalScenarioTests
	{
		private readonly ILogger logger = new TraceLogger { Enabled = true };

		#region Case Sensitivity Tests

		[TestMethod]
		public void ApplicationName_MixedCase_ResolvesCorrectly()
		{
			// URL hostnames are automatically lowercased by .NET Uri class
			// This test ensures case-insensitive lookups work correctly
			var testCases = new[]
			{
				"https://ServiceA.methodic.online",
				"https://servicea.methodic.online", 
				"https://SERVICEA.methodic.online",
				"https://SeRvIcEa.methodic.online"
			};

			using (var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true))
			{
				foreach (var url in testCases)
				{
					// Extract application name
					bool extracted = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
						url, 
						ApplicationNameLocation.Subdomain, 
						out string appName);
					
					Assert.IsTrue(extracted, $"Failed to extract application name from: {url}");

					// Verify all variations resolve to the same endpoint
					Guid invocationId = Guid.NewGuid();
					string endpoint = resolver.ResolveServiceUri(appName, invocationId).GetAwaiter().GetResult();
					
					Assert.IsNotNull(endpoint, $"Failed to resolve endpoint for: {url}");
					Assert.IsTrue(endpoint.Contains("servicea"), $"Expected 'servicea' in endpoint but got: {endpoint}");
				}
			}
		}

		[TestMethod]
		public void PullRequestPattern_MixedCase_TransformsCorrectly()
		{
			// Verify PR pattern works regardless of case
			var testCases = new Dictionary<string, string>
			{
				{ "https://guard-12906.pav.meth.wtf", "Guard-PR12906" },
				{ "https://GUARD-12906.pav.meth.wtf", "Guard-PR12906" },
				{ "https://GuArD-12906.pav.meth.wtf", "Guard-PR12906" }
			};

			foreach (var kvp in testCases)
			{
				bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
					kvp.Key, 
					ApplicationNameLocation.Subdomain, 
					out string appName);
				
				Assert.IsTrue(result, $"Failed to parse: {kvp.Key}");
				Assert.AreEqual(kvp.Value, appName, $"Incorrect transformation for: {kvp.Key}");
			}
		}

		#endregion

		#region Configuration Validation Tests

		[TestMethod]
		public void Initialize_MissingClusterConnectionStrings_ThrowsException()
		{
			var locator = new ServiceFabricLocator(logger);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				// Missing ClusterConnectionStrings - should throw
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			Assert.ThrowsExactly<ArgumentException>(() => locator.Initialize(settings, context));
		}

		[TestMethod]
		public void Initialize_EmptyClusterConnectionStrings_ThrowsException()
		{
			var locator = new ServiceFabricLocator(logger);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "" }, // Empty - should throw
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			Assert.ThrowsExactly<ArgumentException>(() => locator.Initialize(settings, context));
		}

		[TestMethod]
		public void Initialize_WhitespaceClusterConnectionStrings_ThrowsException()
		{
			var locator = new ServiceFabricLocator(logger);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "   " }, // Whitespace - should throw
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			Assert.ThrowsExactly<ArgumentException>(() => locator.Initialize(settings, context));
		}

		[TestMethod]
		public void Initialize_MultipleClusterEndpoints_ParsesCorrectly()
		{
			// Test comma-separated cluster endpoints (production HA clusters)
			var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
			var locator = new ServiceFabricLocator(logger, resolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "node1:19000,node2:19000,node3:19000" },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			locator.Initialize(settings, context);

			Assert.IsTrue(locator.ClusterConnectionStrings.Contains("node1:19000"));
			Assert.IsTrue(locator.ClusterConnectionStrings.Contains("node2:19000"));
			Assert.IsTrue(locator.ClusterConnectionStrings.Contains("node3:19000"));
		}

		[TestMethod]
		public void Initialize_AllApplicationNameLocationModes_Work()
		{
			// Verify all enum values are supported
			var modes = new[]
			{
				ApplicationNameLocation.Subdomain,
				ApplicationNameLocation.SubdomainPreHyphens,
				ApplicationNameLocation.SubdomainPostHyphens,
				ApplicationNameLocation.FirstUrlFragment
			};

			foreach (var mode in modes)
			{
				var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
				var locator = new ServiceFabricLocator(logger, resolver);
				var context = new FakeRewriteContext { RewriteCacheEnabled = false };
				var settings = new Dictionary<string, string>
				{
					{"ClusterConnectionStrings", "localhost:19000" },
					{"ApplicationNameLocation", mode.ToString() },
					{"EnableLogging", "true" },
					{"CredentialsType", "Local" }
				};

				locator.Initialize(settings, context);

				Assert.AreEqual(mode, locator.ApplicationNameLocation, $"Failed to initialize with mode: {mode}");
			}
		}

		#endregion

		#region Error Handling Tests

		[TestMethod]
		public async Task ResolveServiceUri_ApplicationNotFound_ThrowsKeyNotFoundException()
		{
			using (var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true))
			{
				// Try to resolve an application that doesn't exist in mock data
				await Assert.ThrowsExactlyAsync<KeyNotFoundException>(async () =>
				{
					await resolver.ResolveServiceUri("NonExistentApplication", Guid.NewGuid());
				});
			}
		}

		[TestMethod]
		public void Rewrite_EmptyUrl_ThrowsArgumentException()
		{
			var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
			var locator = new ServiceFabricLocator(logger, resolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "localhost:19000" },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			locator.Initialize(settings, context);
			Assert.ThrowsExactly<ArgumentException>(() => locator.Rewrite("")); // Empty URL should throw
		}

		[TestMethod]
		public void Rewrite_NullUrl_ThrowsArgumentNullException()
		{
			var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
			var locator = new ServiceFabricLocator(logger, resolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "localhost:19000" },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			locator.Initialize(settings, context);
			Assert.ThrowsExactly<ArgumentNullException>(() => locator.Rewrite(null)); // Null URL should throw
		}

		[TestMethod]
		public void Rewrite_InvalidUrl_ThrowsArgumentException()
		{
			var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
			var locator = new ServiceFabricLocator(logger, resolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "localhost:19000" },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			locator.Initialize(settings, context);
			Assert.ThrowsExactly<ArgumentException>(() => locator.Rewrite("not-a-valid-url")); // Invalid URL structure
		}

		#endregion

		#region IPv6 and Special Address Tests

		[TestMethod]
		public void EndpointJsonToText_IPv6Loopback_ParsesCorrectly()
		{
			string input = @"{""Endpoints"":{"""": ""https://[::1]:8080""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://[::1]:8080", result);
		}

		[TestMethod]
		public void EndpointJsonToText_IPv6Address_ParsesCorrectly()
		{
			string input = @"{""Endpoints"":{"""": ""https://[2001:0db8:85a3:0000:0000:8a2e:0370:7334]:8080""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://[2001:0db8:85a3:0000:0000:8a2e:0370:7334]:8080", result);
		}

		[TestMethod]
		public void NormalizeLocalEndpoint_IPv6Any_ReplacesWithLoopback()
		{
			string input = "https://[::]:8080/api";
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint(input, logger);
			
			Assert.AreEqual("https://[::1]:8080/api", result);
		}

		#endregion

		#region Port Number Edge Cases

		[TestMethod]
		public void EndpointJsonToText_StandardPorts_Work()
		{
			var testCases = new Dictionary<string, string>
			{
				{ @"{""Endpoints"":{"""": ""https://service.com:443""}}", "https://service.com:443" },
				{ @"{""Endpoints"":{"""": ""http://service.com:80""}}", "http://service.com:80" },
				{ @"{""Endpoints"":{"""": ""https://service.com:8080""}}", "https://service.com:8080" },
				{ @"{""Endpoints"":{"""": ""https://service.com:8443""}}", "https://service.com:8443" },
			};

			foreach (var kvp in testCases)
			{
				string result = ServiceFabricUrlParser.EndpointJsonToText(kvp.Key, logger);
				Assert.AreEqual(kvp.Value, result);
			}
		}

		[TestMethod]
		public void EndpointJsonToText_HighPortNumber_Works()
		{
			string input = @"{""Endpoints"":{"""": ""https://service.com:65535""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://service.com:65535", result);
		}

		#endregion

		#region Authentication Mode Tests

		[TestMethod]
		public void Initialize_LocalAuthentication_Works()
		{
			var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
			var locator = new ServiceFabricLocator(logger, resolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "localhost:19000" },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
			};

			locator.Initialize(settings, context);

			Assert.AreEqual(CredentialsType.Local, locator.CredentialsType);
		}

		[TestMethod]
		public void Initialize_CertificateCommonName_RequiresAllFields()
		{
			var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true);
			var locator = new ServiceFabricLocator(logger, resolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "localhost:19000" },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "CertificateCommonName" },
				{"ClientCertificateSubjectDistinguishedName", "CN=Test" },
				{"ClientCertificateIssuerDistinguishedName", "CN=TestCA" },
				{"ServerCertificateCommonNames", "CN=*.test.com" }
			};

			locator.Initialize(settings, context);

			Assert.AreEqual(CredentialsType.CertificateCommonName, locator.CredentialsType);
		}

		#endregion

		#region Concurrent Access Tests

		[TestMethod]
		public async Task ResolveServiceUri_ConcurrentAccess_ThreadSafe()
		{
			// Test that multiple threads can safely access the resolver
			using (var resolver = TestableServiceFabricUrlResolver.Create(logger, "localhost:19000", useMockData: true))
			{
				var tasks = new List<Task<string>>();

				// Spawn 50 concurrent resolution requests
				for (int i = 0; i < 50; i++)
				{
					tasks.Add(resolver.ResolveServiceUri("ServiceA", Guid.NewGuid()));
					tasks.Add(resolver.ResolveServiceUri("ServiceB", Guid.NewGuid()));
				}

				// Wait for all to complete
				var results = await Task.WhenAll(tasks);

				// Verify all succeeded
				Assert.AreEqual(100, results.Length);
				foreach (var result in results)
				{
					Assert.IsNotNull(result);
					Assert.IsTrue(result.StartsWith("https://"));
				}

				// Verify cache was populated correctly
				Assert.AreEqual(2, resolver.MockCachedApplicationCount);
			}
		}

		#endregion

		#region Real-World URL Pattern Tests

		[TestMethod]
		public void RealWorldUrls_AllPatterns_Work()
		{
			// Test real-world URL patterns from various deployment scenarios
			var testCases = new Dictionary<string, (ApplicationNameLocation mode, string expected)>
			{
				// Production subdomain patterns
				{ "https://api.prod.company.com", (ApplicationNameLocation.Subdomain, "api") },
				{ "https://users.staging.company.com/v1/health", (ApplicationNameLocation.Subdomain, "users") },
				
				// PR environments
				{ "https://api-1234.preview.company.com", (ApplicationNameLocation.Subdomain, "Api-PR1234") },
				{ "https://users-5678.preview.company.com/swagger", (ApplicationNameLocation.Subdomain, "Users-PR5678") },
				
				// Legacy hyphenated patterns
				{ "https://api-v2-prod.company.com", (ApplicationNameLocation.SubdomainPreHyphens, "api") },
				{ "https://prod-api.company.com", (ApplicationNameLocation.SubdomainPostHyphens, "api") },
				
				// Path-based routing
				{ "https://gateway.company.com/api", (ApplicationNameLocation.FirstUrlFragment, "api") },
				{ "https://gateway.company.com/users/v1", (ApplicationNameLocation.FirstUrlFragment, "users") },
				
				// With query parameters
				{ "https://api.company.com?version=v1&key=abc", (ApplicationNameLocation.Subdomain, "api") },
				
				// With ports
				{ "https://api.company.com:8443/health", (ApplicationNameLocation.Subdomain, "api") },
				
				// Mixed case (hostnames are lowercased by Uri class)
				{ "HTTPS://API.COMPANY.COM", (ApplicationNameLocation.Subdomain, "api") }
			};

			foreach (var kvp in testCases)
			{
				bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
					kvp.Key, 
					kvp.Value.mode, 
					out string appName);
				
				Assert.IsTrue(result, $"Failed to parse: {kvp.Key}");
				Assert.AreEqual(kvp.Value.expected, appName, $"Wrong app name for: {kvp.Key}");
			}
		}

		#endregion

		public class FakeRewriteContext : Microsoft.Web.Iis.Rewrite.IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }

			public void ClearRewriteCache()
			{
				// No-op for testing
			}
		}
	}
}
