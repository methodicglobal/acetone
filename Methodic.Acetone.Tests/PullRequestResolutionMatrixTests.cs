using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	[TestCategory("PullRequestRouting")] 
	public class PullRequestResolutionMatrixTests
	{
		private readonly ILogger logger = new TraceLogger { Enabled = true };

		private ServiceFabricLocator BuildLocator(MockServiceFabricUrlResolver resolver)
		{
			var locator = new ServiceFabricLocator(logger, resolver);
			var ctx = new FakeRewriteContext { RewriteCacheEnabled = false };
			locator.Initialize(new Dictionary<string,string>
			{
				{"ClusterConnectionStrings","localhost:19000"},
				{"ApplicationNameLocation","Subdomain"},
				{"EnableLogging","true"},
				{"CredentialsType","Local"}
			}, ctx);
			return locator;
		}

		/// <summary>
		/// Scenario 1: Only base application deployed (Guard). PR requests should fail.
		/// </summary>
		[TestMethod]
		public void OnlyBaseApplication_PrRequestFails()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			resolver.AddMockApplication("Guard", "https://guard.pav.test", false);
			var locator = BuildLocator(resolver);

			// Base resolves
			Assert.AreEqual("https://guard.pav.test", locator.Rewrite("https://guard.pav.test"));
			// PR missing
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard-12906.pav.test"));
		}

		/// <summary>
		/// Scenario 2: Only PR application deployed (Guard_PR12906). Base request should fail.
		/// </summary>
		[TestMethod]
		public void OnlySinglePrApplication_BaseRequestFails()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			resolver.AddMockApplication("Guard_PR12906", "https://guard-12906.pav.test", true);
			var locator = BuildLocator(resolver);

			// PR resolves (hyphen style incoming => underscore in cluster)
			Assert.AreEqual("https://guard-12906.pav.test", locator.Rewrite("https://guard-12906.pav.test"));
			// Base missing
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard.pav.test"));
		}

		/// <summary>
		/// Scenario 3: Many PRs plus base. All existing PRs resolve; unknown PR fails.
		/// </summary>
		[TestMethod]
		public void MultiplePrApplications_AllResolve_UnknownFails()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			resolver.AddMockApplication("Guard", "https://guard.pav.test", false);
			foreach (var pr in new []{"100","101","12906","20000"})
			{
				resolver.AddMockApplication($"Guard_PR{pr}", $"https://guard-{pr}.pav.test", true);
			}
			var locator = BuildLocator(resolver);

			// Base
			Assert.AreEqual("https://guard.pav.test", locator.Rewrite("https://guard.pav.test"));
			// All PRs
			foreach (var pr in new []{"100","101","12906","20000"})
			{
				Assert.AreEqual($"https://guard-{pr}.pav.test", locator.Rewrite($"https://guard-{pr}.pav.test"), $"PR {pr} should resolve");
			}
			// Unknown PR
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard-9999.pav.test"));
		}

		/// <summary>
		/// Scenario 4: Underscore & hyphen coexist; ensure normalization still hits correct endpoint.
		/// </summary>
		[TestMethod]
		public void MixedHyphenUnderscoreNaming_NormalizationWorks()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			resolver.AddMockApplication("Guard_PR123", "https://guard-123.pav.test", true); // underscore variant
			resolver.AddMockApplication("Guard-PR456", "https://guard-456.pav.test", true); // hyphen stored variant
			resolver.AddMockApplication("Guard", "https://guard.pav.test", false);
			var locator = BuildLocator(resolver);

			// Request hyphen for underscore app
			Assert.AreEqual("https://guard-123.pav.test", locator.Rewrite("https://guard-123.pav.test"));
			// Request hyphen for hyphen app
			Assert.AreEqual("https://guard-456.pav.test", locator.Rewrite("https://guard-456.pav.test"));
		}

		/// <summary>
		/// Scenario 5: No applications deployed; any request fails with KeyNotFound.
		/// </summary>
		[TestMethod]
		public void NoApplications_AllRequestsFail()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			var locator = BuildLocator(resolver);
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard.pav.test"));
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard-1234.pav.test"));
		}

		/// <summary>
		/// Scenario 6: Strange names present that should not accidentally match PR transform.
		/// </summary>
		[TestMethod]
		public void NonDigitSuffix_NotTransformed()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			resolver.AddMockApplication("Guard-PRChaos", "https://guard-chaos.pav.test", true); // Non-standard cluster naming
			var locator = BuildLocator(resolver);

			// Incoming guard-chaos -> extracted name 'guard-chaos' (no PR digits) => should not match Guard-PRChaos and fail
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard-chaos.pav.test"));
		}

		/// <summary>
		/// Scenario 7: Ensure partition between numeral and leading zeros is respected.
		/// guard-001 should resolve only if Guard_PR001 exists, not reuse Guard_PR1.
		/// </summary>
		[TestMethod]
		public void LeadingZeroPrNumbers_Distinct()
		{
			var resolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			resolver.ClearApplications();
			resolver.AddMockApplication("Guard_PR1", "https://guard-1.pav.test", true);
			resolver.AddMockApplication("Guard_PR001", "https://guard-001.pav.test", true);
			var locator = BuildLocator(resolver);

			Assert.AreEqual("https://guard-1.pav.test", locator.Rewrite("https://guard-1.pav.test"));
			Assert.AreEqual("https://guard-001.pav.test", locator.Rewrite("https://guard-001.pav.test"));
		}

		private class FakeRewriteContext : Microsoft.Web.Iis.Rewrite.IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }
			public void ClearRewriteCache() { }
		}
	}
}
