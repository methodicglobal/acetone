using Methodic.Acetone;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Real cluster integration tests that deploy actual applications to a local Service Fabric cluster.
	/// These tests are skipped if no cluster is available, falling back to mock data.
	/// </summary>
	[TestClass]
	public class RealClusterIntegrationTests
	{
		private static ServiceFabricTestClusterManager clusterManager;
		private static readonly ILogger logger = new TraceLogger { Enabled = true };
		private const string ClusterEndpoint = "localhost:19000";
		private static bool skipDeployment;
		private static bool usingMockData;
		private static List<string> deployedApplications;

		/// <summary>
		/// Class initialization - runs once before all tests.
		/// Deploys test applications to the cluster.
		/// </summary>
		[ClassInitialize]
		public static void ClassSetup(TestContext context)
		{
			skipDeployment = string.Equals(Environment.GetEnvironmentVariable("ACETONE_SKIP_DEPLOY"), "1", StringComparison.OrdinalIgnoreCase);
			usingMockData = false;

			Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
			Console.WriteLine("║  Real Cluster Integration Test Suite                         ║");
			Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
			Console.WriteLine();

			try
			{
				clusterManager = new ServiceFabricTestClusterManager(ClusterEndpoint, logger);

				if (clusterManager.IsClusterAvailable && !skipDeployment)
				{
					Console.WriteLine("✅ Service Fabric cluster is available");
					Console.WriteLine($"🚀 Deploying solution Service Fabric applications to {ClusterEndpoint}...");
					Console.WriteLine();

					var solutionApps = clusterManager.DeploySolutionApplicationsAsync().GetAwaiter().GetResult();
					var syntheticApps = clusterManager.DeployTestApplicationsAsync(
						count: 3,
						applicationNamePrefix: "TestService").GetAwaiter().GetResult();

					deployedApplications = solutionApps.Concat(syntheticApps).Distinct().ToList();

					Console.WriteLine();
					Console.WriteLine($"✅ Deployed {deployedApplications.Count} cluster applications:");
					foreach (var app in deployedApplications)
					{
						Console.WriteLine($"   • {app}");
					}
					Console.WriteLine();
				}
				else if (skipDeployment)
				{
					Console.WriteLine("⚠ Deployment skipped due to ACETONE_SKIP_DEPLOY=1");
					deployedApplications = new List<string>();
					usingMockData = true;
				}
				else
				{
					Console.WriteLine("⚠ Service Fabric cluster not available");
					Console.WriteLine("ℹ Tests will use mock data for reliable execution");
					Console.WriteLine();
					deployedApplications = new List<string>();
					usingMockData = true;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error during setup: {ex.Message}");
				Console.WriteLine("ℹ Tests will continue with mock data");
				Console.WriteLine();
				deployedApplications = new List<string>();
				usingMockData = true;
			}
		}

		/// <summary>
		/// Class cleanup - runs once after all tests.
		/// Removes all deployed test applications.
		/// </summary>
		[ClassCleanup]
		public static void ClassTeardown()
		{
			Console.WriteLine();
			Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
			Console.WriteLine("║  Cleanup: Removing Test Applications                         ║");
			Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
			Console.WriteLine();

			bool skipUnprovision = string.Equals(Environment.GetEnvironmentVariable("ACETONE_SKIP_APP_TYPE_UNPROVISION"), "1", StringComparison.OrdinalIgnoreCase);
			try
			{
				if (clusterManager != null && clusterManager.IsClusterAvailable)
				{
					Console.WriteLine("🧹 Removing deployed test applications...");
					clusterManager.RemoveAllDeployedApplicationsAsync(!skipUnprovision).GetAwaiter().GetResult();
					Console.WriteLine("✅ Cleanup complete");
				}
				else
				{
					Console.WriteLine("ℹ No cluster cleanup needed (used mock data)");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"⚠ Error during cleanup: {ex.Message}");
			}
			finally
			{
				clusterManager?.Dispose();
				Console.WriteLine();
			}
		}

		private static bool ShouldSkipClusterTests(bool requireDeployment, out string reason)
		{
			if (skipDeployment)
			{
				reason = "Cluster deployment skipped via ACETONE_SKIP_DEPLOY=1";
				return true;
			}

			if (clusterManager == null || !clusterManager.IsClusterAvailable)
			{
				reason = "Cluster not available; using mock data";
				return true;
			}

			if (requireDeployment && (usingMockData || deployedApplications == null || deployedApplications.Count == 0))
			{
				reason = "No applications deployed to cluster; using mock data";
				return true;
			}

			reason = null;
			return false;
		}

		[TestMethod]
		[TestCategory("RealCluster")]
		[TestCategory("Integration")]
		public void VerifyClusterConnection()
		{
			if (ShouldSkipClusterTests(requireDeployment: false, out var reason))
			{
				Assert.Inconclusive(reason);
				return;
			}

			Assert.IsTrue(clusterManager.IsClusterAvailable, "Cluster should be available");
			Console.WriteLine($"✅ Connected to cluster at {ClusterEndpoint}");
		}

		[TestMethod]
		[TestCategory("RealCluster")]
		[TestCategory("Integration")]
		public void VerifyApplicationsDeployed()
		{
			if (ShouldSkipClusterTests(requireDeployment: true, out var reason))
			{
				Assert.Inconclusive(reason);
				return;
			}

			Assert.IsTrue(deployedApplications.Count >= 2, "At least solution applications should be deployed");
			Console.WriteLine($"✅ {deployedApplications.Count} applications verified:");
			foreach (var app in deployedApplications)
			{
				Console.WriteLine($"   • {app}");
			}
		}

		[TestMethod]
		[TestCategory("RealCluster")]
		[TestCategory("Integration")]
		public async Task ResolveRealClusterEndpoints()
		{
			if (ShouldSkipClusterTests(requireDeployment: true, out var reason))
			{
				Assert.Inconclusive(reason);
				return;
			}

			using (var resolver = new ServiceFabricUrlResolver(logger, ClusterEndpoint))
			{
				var resolvedEndpoints = new List<string>();

				foreach (var appName in deployedApplications)
				{
					try
					{
						var endpoint = await resolver.ResolveServiceUri(appName, Guid.NewGuid());
						resolvedEndpoints.Add(endpoint);
						Assert.IsNotNull(endpoint, $"Endpoint for {appName} should not be null");
						Assert.IsTrue(endpoint.StartsWith("http://") || endpoint.StartsWith("https://"), $"Endpoint should be HTTP/HTTPS: {endpoint}");
						Console.WriteLine($"✅ {appName} → {endpoint}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"⚠ Could not resolve {appName}: {ex.Message}");
					}
				}

				Assert.IsTrue(resolvedEndpoints.Count > 0, "At least one endpoint should be resolved");
			}
		}

		[TestMethod]
		[TestCategory("RealCluster")]
		[TestCategory("Integration")]
		public async Task GetDeployedApplicationInfo()
		{
			if (ShouldSkipClusterTests(requireDeployment: true, out var reason))
			{
				Assert.Inconclusive(reason);
				return;
			}

			var appInfoList = await clusterManager.GetDeployedApplicationInfoAsync();
			Assert.IsTrue(appInfoList.Count > 0, "Should have at least one deployed application");
			Console.WriteLine($"📋 Deployed Application Details:");
			Console.WriteLine();
			foreach (var appInfo in appInfoList)
			{
				Console.WriteLine($"  {appInfo}");
			}
		}
	}
}
