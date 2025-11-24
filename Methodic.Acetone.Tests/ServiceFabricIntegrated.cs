using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Web.Iis.Rewrite;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	public class ServiceFabricIntegrated
	{
		private static readonly ILogger logger = new TraceLogger { Enabled = true };
		private const string ClusterEndpoint = "localhost:19000";
		private const int ServiceDeploymentCount = 4; // Reduced synthetic deployments; solution apps added separately
		private static bool useRealCluster;
		private static ServiceFabricTestClusterManager clusterManager;
		private static List<string> servicesUnderTest = new List<string>();
		private static readonly List<string> mockServices = new List<string> { "ServiceA", "ServiceB", "Guard", "ServiceA-PR1234" };

		private static (IServiceUrlResolver resolver, IDisposable disposable) CreateResolver(ILogger instanceLogger)
		{
			if (useRealCluster)
			{
				var realResolver = new ServiceFabricUrlResolver(instanceLogger, ClusterEndpoint);
				return (realResolver, realResolver);
			}

			var mockResolver = new MockServiceFabricUrlResolver(instanceLogger, ClusterEndpoint);
			return (mockResolver, mockResolver);
		}

		[ClassInitialize]
		public static async Task ClassSetup(TestContext context)
		{
			Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
			Console.WriteLine("║  Service Fabric Integration Tests                            ║");
			Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
			Console.WriteLine();

			Console.WriteLine(ClusterAvailabilityHelper.GetAvailabilityMessage());
			Console.WriteLine();

			bool skipDeployment = string.Equals(Environment.GetEnvironmentVariable("ACETONE_SKIP_DEPLOY"), "1", StringComparison.OrdinalIgnoreCase);
			useRealCluster = !skipDeployment && ClusterAvailabilityHelper.IsClusterAvailable("localhost", 19000);
			if (useRealCluster)
			{
				try
				{
					clusterManager = new ServiceFabricTestClusterManager(ClusterEndpoint, logger);
					useRealCluster = clusterManager.IsClusterAvailable;

					if (useRealCluster)
					{
						Console.WriteLine($"🚀 Detected local Service Fabric cluster at {ClusterEndpoint}, deploying solution applications + {ServiceDeploymentCount} synthetic test apps...");
						var solutionApps = await clusterManager.DeploySolutionApplicationsAsync();
						var syntheticApps = await clusterManager.DeployTestApplicationsAsync(ServiceDeploymentCount, "AcetoneIntegrationTest");
						servicesUnderTest = solutionApps.Concat(syntheticApps).Distinct().ToList();

						if (servicesUnderTest.Count > 0)
						{
							Console.WriteLine($"✅ Deployed {servicesUnderTest.Count} applications: {string.Join(", ", servicesUnderTest)}");
						}
						else
						{
							Console.WriteLine("⚠ No applications were deployed; falling back to mock mode");
							useRealCluster = false;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"⚠ Failed to prepare real cluster integration environment: {ex.Message}");
					useRealCluster = false;
				}
			}
			else if (skipDeployment)
			{
				Console.WriteLine("⚠ Skipping deployment due to ACETONE_SKIP_DEPLOY=1");
			}

			if (!useRealCluster)
			{
				servicesUnderTest = new List<string>(mockServices);
				Console.WriteLine("ℹ️  Running tests using mock Service Fabric resolver (no cluster required)");
			}

			Console.WriteLine("────────────────────────────────────────────────────────────────");
			Console.WriteLine();

			await Task.CompletedTask;
		}

		[ClassCleanup]
		public static void ClassCleanup()
		{
			if (clusterManager != null && useRealCluster)
			{
				try
				{
					Console.WriteLine();
					Console.WriteLine("🧹 Cleaning up deployed test applications...");
					bool skipUnprovision = string.Equals(Environment.GetEnvironmentVariable("ACETONE_SKIP_APP_TYPE_UNPROVISION"), "1", StringComparison.OrdinalIgnoreCase);
					clusterManager.RemoveAllDeployedApplicationsAsync(!skipUnprovision).GetAwaiter().GetResult();
					Console.WriteLine("✅ Cleanup complete");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"⚠ Failed to clean up test applications: {ex.Message}");
				}
				finally
				{
					clusterManager.Dispose();
					clusterManager = null;
				}
			}
		}

		[TestInitialize]
		public void TestSetup()
		{
			if (useRealCluster)
			{
				Console.WriteLine("✅ Running against REAL Service Fabric cluster (deployments provisioned)");
			}
			else
			{
				Console.WriteLine("ℹ️  Running with MOCK data (fast, reliable integration testing)");
			}
		}

		[TestMethod]
		public void EndpointResolutionSuccess()
		{
			if (!useRealCluster)
			{
				Assert.Inconclusive("Performance benchmark skipped when using mock resolver");
				return;
			}

			if (servicesUnderTest == null || servicesUnderTest.Count == 0)
			{
				Assert.Inconclusive("No services available for resolution tests");
				return;
			}

			int numberOfTests = useRealCluster ? Math.Max(servicesUnderTest.Count * 40, 200) : 10000;
			var random = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
			ConcurrentBag<long> times = new ConcurrentBag<long>();

			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				Parallel.For(0, numberOfTests, _ =>
				{
					int serviceIndex = random.Value.Next(0, servicesUnderTest.Count);
					var sw = Stopwatch.StartNew();
					resolver.ResolveServiceUri(servicesUnderTest[serviceIndex], Guid.Empty).GetAwaiter().GetResult();
					sw.Stop();
					times.Add(sw.ElapsedMilliseconds);
				});
			}
			double averageTime = times.Average();
			Console.WriteLine($"Cached {ServiceFabricUrlResolver.CachedApplicationCount} applications and {ServiceFabricUrlResolver.CachedServicesCount} services");
			Console.WriteLine($"Average time for ResolveServiceUri is {averageTime} milliseconds across {times.Count} measured calls");

			double expected = useRealCluster ? 300 : 100; // Allow a little more headroom for real cluster
			Assert.IsTrue(averageTime < expected, $"Expected ResolveServiceUri to average under {expected} milliseconds per call over {times.Count} calls but was {averageTime} instead");
		}

		[TestMethod]
		public async Task SingleEndpointResolution()
		{
			if (servicesUnderTest == null || servicesUnderTest.Count == 0)
			{
				Assert.Inconclusive("No services available for resolution tests");
				return;
			}

			string serviceName = servicesUnderTest[0];
			Guid invocationId = Guid.NewGuid();

			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				var serviceUri = await resolver.ResolveServiceUri(serviceName, invocationId);
				Assert.IsFalse(string.IsNullOrWhiteSpace(serviceUri), "Service URI should not be empty");
				Assert.IsTrue(
					serviceUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
					serviceUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
					$"Endpoint should be HTTP/S but was '{serviceUri}' for {serviceName}");

				if (resolver is MockServiceFabricUrlResolver mockResolver && mockResolver.GetApplications().TryGetValue(serviceName, out var mockApp))
				{
					Assert.AreEqual(mockApp.Endpoint, serviceUri, "Mock resolver should return configured endpoint");
				}
			}
		}

		[TestMethod]
		public async Task SecureEndpointResolution()
		{
			if (servicesUnderTest == null || servicesUnderTest.Count == 0)
			{
				Assert.Inconclusive("No services available for resolution tests");
				return;
			}

			string serviceName = servicesUnderTest[Math.Min(1, servicesUnderTest.Count - 1)];
			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				var serviceUri = await resolver.ResolveServiceUri(serviceName, Guid.NewGuid());
				Assert.IsTrue(Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri), $"Endpoint should be a valid URI but was '{serviceUri}'");
				Assert.IsTrue(uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
					$"Expected http or https scheme but found '{uri.Scheme}'");
			}
		}

		[TestMethod]
		public async Task CachedEndpointResolutionTest()
		{
			if (servicesUnderTest == null || servicesUnderTest.Count == 0)
			{
				Assert.Inconclusive("No services available for resolution tests");
				return;
			}

			var resolvedUrls = new List<string>();
			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				Guid invocationId = Guid.NewGuid();
				string firstService = servicesUnderTest.First();
				string secondService = servicesUnderTest[Math.Min(1, servicesUnderTest.Count - 1)];

				var first = await resolver.ResolveServiceUri(firstService, invocationId);
				var second = await resolver.ResolveServiceUri(firstService, invocationId);
				var third = await resolver.ResolveServiceUri(secondService, invocationId);

				resolvedUrls.AddRange(new[] { first, second, third });

				Assert.AreEqual(first, second, "Repeated resolution of the same service should return the same endpoint");

				if (resolver is MockServiceFabricUrlResolver mockResolver)
				{
					Assert.IsTrue(mockResolver.GetApplications().ContainsKey(firstService));
				}
				else
				{
					Assert.IsTrue(ServiceFabricUrlResolver.CachedApplicationCount >= 1, "Expected resolver cache to contain at least one application");
				}
			}
		}

		[TestMethod]
		public void CheckInitialization()
		{
			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				var rewriter = new ServiceFabricLocator(logger, resolver);
				var context = new FakeRewriteContext { RewriteCacheEnabled = false };
				var settings = new Dictionary<string, string>
				{
					{"ClusterConnectionStrings", ClusterEndpoint },
					{"ApplicationNameLocation", "Subdomain" },
					{"EnableLogging", "true" },
					{"CredentialsType", "Local" }
				};

				rewriter.Initialize(settings, context);

				Assert.IsTrue(rewriter.ClusterConnectionStrings?.Contains(ClusterEndpoint));
				Assert.AreEqual(rewriter.ApplicationNameLocation, ApplicationNameLocation.Subdomain);
				Assert.IsFalse(rewriter.RewriteContext.RewriteCacheEnabled);
			}
		}

		[TestMethod]
		public void CheckRewriteWithCommonNames()
		{
			if (servicesUnderTest == null || servicesUnderTest.Count == 0)
			{
				Assert.Inconclusive("No services available for rewrite tests");
				return;
			}

			string serviceName = servicesUnderTest.First();
			string url = $"https://{serviceName.ToLowerInvariant()}.methodic.online";
			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				var rewriter = new ServiceFabricLocator(logger, resolver);
				var context = new FakeRewriteContext { RewriteCacheEnabled = true };
				var settings = new Dictionary<string, string>
				{
					{"ClusterConnectionStrings", ClusterEndpoint },
					{"ApplicationNameLocation", "Subdomain" },
					{"EnableLogging", "true" },
					{"CredentialsType", "CertificateCommonName" },
					{"ClientCertificateSubjectDistinguishedName", "E=info@methodic.com, CN=Methodic Global, CN=Users, DC=methodic, DC=online" },
					{"ClientCertificateIssuerDistinguishedName", "CN=Methodic-Test-Certificate-Authority, DC=methodic, DC=online" },
					{ "ServerCertificateCommonNames", "CN=*.methodic.online" }
				};
				rewriter.Initialize(settings, context);
				var redirectUrl = rewriter.Rewrite(url);
				Assert.IsNotNull(redirectUrl);
			}
		}

		[TestMethod]
		public void CheckFinalRewrite()
		{
			if (servicesUnderTest == null || servicesUnderTest.Count == 0)
			{
				Assert.Inconclusive("No services available for rewrite tests");
				return;
			}

			string serviceName = servicesUnderTest.First();
			string url = $"https://{serviceName.ToLowerInvariant()}.methodic.online";
			var (resolver, disposable) = CreateResolver(logger);
			using (disposable)
			{
				var rewriter = new ServiceFabricLocator(logger, resolver);
				var context = new FakeRewriteContext { RewriteCacheEnabled = true };
				var settings = new Dictionary<string, string>
				{
					{"ClusterConnectionStrings", ClusterEndpoint },
					{"ApplicationNameLocation", "Subdomain" },
					{"EnableLogging", "true" },
					{"CredentialsType", "Local" }
				};
				rewriter.Initialize(settings, context);
				var redirectUrl = rewriter.Rewrite(url);
				Assert.IsNotNull(redirectUrl);
			}
		}

		[TestMethod]
		public void CheckPullRequestUrlExtraction()
		{
			string prUrl = "https://guard-12906.pav.meth.wtf";
			if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(prUrl, ApplicationNameLocation.Subdomain, out string applicationName))
			{
				Assert.Fail("Could not resolve application name from PR URL");
			}
			Assert.AreEqual("Guard-PR12906", applicationName, "Pull request URL should be transformed to Guard-PR12906");

			string normalUrl = "https://guard.pav.meth.wtf";
			if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(normalUrl, ApplicationNameLocation.Subdomain, out string normalAppName))
			{
				Assert.Fail("Could not resolve application name from normal URL");
			}
			Assert.AreEqual("guard", normalAppName, "Normal URL should extract service name as-is");
		}

		public class FakeRewriteContext : IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }
			public void ClearRewriteCache()
			{
				throw new NotImplementedException();
			}
		}
	}
}
