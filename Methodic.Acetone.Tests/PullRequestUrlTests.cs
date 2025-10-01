using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	public class PullRequestUrlTests
	{
		[TestMethod]
		public void TestPullRequestUrlParsing_SubdomainMode()
		{
			// Test the exact use case: guard-12906.pav.meth.wtf -> Guard-PR12906
			bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
				"https://guard-12906.pav.meth.wtf", 
				ApplicationNameLocation.Subdomain, 
				out string applicationName);

			Assert.IsTrue(result, "Should successfully parse PR URL");
			Assert.AreEqual("Guard-PR12906", applicationName, "Should transform guard-12906 to Guard-PR12906");
		}

		[TestMethod]
		public void TestPullRequestUrlParsing_FirstUrlFragmentMode()
		{
			// Test first URL fragment mode with PR pattern
			bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
				"https://api.pav.meth.wtf/guard-12906", 
				ApplicationNameLocation.FirstUrlFragment, 
				out string applicationName);

			Assert.IsTrue(result, "Should successfully parse PR URL in first fragment mode");
			Assert.AreEqual("Guard-PR12906", applicationName, "Should transform guard-12906 to Guard-PR12906");
		}

		[TestMethod]
		public void TestRegularUrlParsing_StillWorks()
		{
			// Test that regular URLs without PR pattern still work
			bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
				"https://guard.pav.meth.wtf", 
				ApplicationNameLocation.Subdomain, 
				out string applicationName);

			Assert.IsTrue(result, "Should successfully parse regular URL");
			Assert.AreEqual("guard", applicationName, "Should return guard as-is for regular URL");
		}

		[TestMethod]
		public void TestNonPRHyphenatedUrl()
		{
			// Test URLs with hyphens that are NOT PR patterns (no numeric suffix)
			bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
				"https://my-service.pav.meth.wtf", 
				ApplicationNameLocation.Subdomain, 
				out string applicationName);

			Assert.IsTrue(result, "Should successfully parse hyphenated non-PR URL");
			Assert.AreEqual("my-service", applicationName, "Should return my-service as-is since it's not a PR pattern");
		}

		[TestMethod]
		public void TestVariousPRPatterns()
		{
			var testCases = new[]
			{
				("https://api-1234.test.methodic.com", "Api-PR1234"),
				("https://service-999.dev.company.com", "Service-PR999"),
				("https://myapp-567890.staging.methodic.com", "Myapp-PR567890"),
				("guard-42.methodic.online", "Guard-PR42")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
					url, 
					ApplicationNameLocation.Subdomain, 
					out string applicationName);

				Assert.IsTrue(result, $"Should successfully parse URL: {url}");
				Assert.AreEqual(expected, applicationName, $"Failed for URL: {url}");
			}
		}

		[TestMethod]
		public void TestPRPatternNotAppliedToOtherModes()
		{
			// PR pattern should only apply to Subdomain and FirstUrlFragment modes
			bool result = ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(
				"https://guard-12906.pav.meth.wtf", 
				ApplicationNameLocation.SubdomainPreHyphens, 
				out string applicationName);

			Assert.IsTrue(result, "Should successfully parse URL");
			Assert.AreEqual("guard", applicationName, "SubdomainPreHyphens should extract 'guard' (before first hyphen), not apply PR transformation");
		}
	}
}