using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	[TestCategory("PullRequestRouting")]
	public class PullRequestRoutingNegativeTests
	{
		private readonly ILogger logger = new TraceLogger { Enabled = true };

		/// <summary>
		/// Reproduces the reported issue: only Guard and Guard-PR12906 exist. A request for guard-999 SHOULD NOT resolve
		/// (it should throw a KeyNotFoundException). Valid base hostname and valid PR hostname must still resolve.
		/// </summary>
		[TestMethod]
		public void GuardPullRequest_InvalidPrNumber_DoesNotResolve()
		{
			// Arrange: mock resolver with ONLY the two real applications
			var mockResolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			mockResolver.ClearApplications();
			mockResolver.AddMockApplication("Guard", "https://guard.pav.local", isPullRequest: false);
			mockResolver.AddMockApplication("Guard-PR12906", "https://guard-12906.pav.local", isPullRequest: true);

			var locator = new ServiceFabricLocator(logger, mockResolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			locator.Initialize(new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", "localhost:19000"},
				{"ApplicationNameLocation", "Subdomain"},
				{"EnableLogging", "true"},
				{"CredentialsType", "Local"}
			}, context);

			// Act & Assert: base hostname resolves
			var guardEndpoint = locator.Rewrite("https://guard.pav.local");
			Assert.AreEqual("https://guard.pav.local", guardEndpoint, "Expected Guard base endpoint");

			// Act & Assert: existing PR resolves
			var guardPrEndpoint = locator.Rewrite("https://guard-12906.pav.local");
			Assert.AreEqual("https://guard-12906.pav.local", guardPrEndpoint, "Expected Guard-PR12906 endpoint");

			// Act & Assert: NON-EXISTENT PR should fail
			Assert.ThrowsExactly<KeyNotFoundException>(() => locator.Rewrite("https://guard-999.pav.local"),
				"A request for an unknown PR number (guard-999) incorrectly resolved instead of throwing.");
		}

		private class FakeRewriteContext : Microsoft.Web.Iis.Rewrite.IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }
			public void ClearRewriteCache() { }
		}
	}
}
