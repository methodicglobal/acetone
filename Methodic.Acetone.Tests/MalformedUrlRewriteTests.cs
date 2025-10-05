using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	[TestCategory("MalformedUrl")] 
	public class MalformedUrlRewriteTests
	{
		private readonly ILogger logger = new TraceLogger { Enabled = true };

		/// <summary>
		/// Reproduces production scenario where IIS appears to pass an URL value containing an extra colon-separated IPv6 tail
		/// e.g. "https://guard-12906.pav.meth.wtf:443:2403:5818:d5fa:10::177/" which currently fails parsing.
		/// Expected behaviour: Acetone should gracefully sanitise this value and still resolve Guard-PR12906.
		/// </summary>
		[TestMethod]
		public void Rewrite_WithAppendedIPv6Tail_StillResolvesPullRequestApplication()
		{
			// Arrange minimal mock resolver with required apps only
			var mockResolver = new MockServiceFabricUrlResolver(logger, "localhost:19000");
			mockResolver.ClearApplications();
			mockResolver.AddMockApplication("Guard", "https://guard.pav.meth.wtf", isPullRequest: false);
			mockResolver.AddMockApplication("Guard-PR12906", "https://guard-12906.pav.meth.wtf", isPullRequest: true);

			var locator = new ServiceFabricLocator(logger, mockResolver);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			locator.Initialize(new Dictionary<string,string>
			{
				{"ClusterConnectionStrings", "localhost:19000"},
				{"ApplicationNameLocation", "Subdomain"},
				{"EnableLogging", "true"},
				{"CredentialsType", "Local"}
			}, context);

			// Malformed value as observed in production error (extra colon + IPv6 style tail without brackets)
			string malformed = "https://guard-12906.pav.meth.wtf:443:2403:5818:d5fa:10::177/";

			// Act
			var endpoint = locator.Rewrite(malformed);

			// Assert
			Assert.AreEqual("https://guard-12906.pav.meth.wtf", endpoint, "Sanitised malformed URL should resolve Guard-PR12906 endpoint");
		}

		private class FakeRewriteContext : Microsoft.Web.Iis.Rewrite.IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }
			public void ClearRewriteCache() { }
		}
	}
}
