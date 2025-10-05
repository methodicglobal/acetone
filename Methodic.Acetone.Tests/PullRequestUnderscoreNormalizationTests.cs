using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	[TestCategory("PullRequestRouting")] 
	public class PullRequestUnderscoreNormalizationTests
	{
		private readonly ILogger logger = new TraceLogger { Enabled = true };

		/// <summary>
		/// Ensures an application deployed with underscore variant (Guard_PR12906) is resolved
		/// when the incoming PR pattern produces Guard-PR12906 (hyphen).
		/// Mirrors production normalization added in ServiceFabricUrlResolver.NormalizeApplicationIdentifier.
		/// </summary>
		[TestMethod]
		public void HyphenToUnderscoreNormalization_AllowsResolution()
		{
			var mockResolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			mockResolver.ClearApplications();
			// Simulate cluster deployment using underscore naming convention
			mockResolver.AddMockApplication("Guard_PR12906", "https://guard-12906.pav.meth.wtf", isPullRequest: true);
			mockResolver.AddMockApplication("Guard", "https://guard.pav.meth.wtf", isPullRequest: false);

			var locator = new ServiceFabricLocator(logger, mockResolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			locator.Initialize(new Dictionary<string,string>
			{
				{"ClusterConnectionStrings", "localhost:19000"},
				{"ApplicationNameLocation", "Subdomain"},
				{"EnableLogging", "true"},
				{"CredentialsType", "Local"}
			}, context);

			// Incoming host with hyphen style PR pattern
			string url = "https://guard-12906.pav.meth.wtf";
			var endpoint = locator.Rewrite(url);
			Assert.AreEqual("https://guard-12906.pav.meth.wtf", endpoint, "Resolver should map hyphen pattern to underscore app name");
		}

		private class FakeRewriteContext : Microsoft.Web.Iis.Rewrite.IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }
			public void ClearRewriteCache() { }
		}
	}
}
