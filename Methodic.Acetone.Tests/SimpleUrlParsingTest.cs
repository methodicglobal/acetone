using System;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Simple standalone test for URL parsing functionality that doesn't require Service Fabric SDK
	/// </summary>
	public class SimpleUrlParsingTest
	{
		public static void RunTests()
		{
			Console.WriteLine("=== Testing Pull Request URL Parsing ===");
			
			bool allTestsPassed = true;

			// Test cases for PR URL parsing
			var testCases = new[]
			{
				("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.Subdomain, "Guard-PR12906"),
				("https://api-1234.test.methodic.com", ApplicationNameLocation.Subdomain, "Api-PR1234"),
				("guard-999.methodic.online", ApplicationNameLocation.Subdomain, "Guard-PR999"),
				("https://api.methodic.com/service-5678", ApplicationNameLocation.FirstUrlFragment, "Service-PR5678"),
				("https://guard.pav.meth.wtf", ApplicationNameLocation.Subdomain, "guard"), // Regular URL
				("https://my-service.methodic.com", ApplicationNameLocation.Subdomain, "my-service"), // Hyphen but no number
			};

			foreach (var (url, mode, expected) in testCases)
			{
				Console.WriteLine($"\nTesting: {url} with mode {mode}");
				
				if (ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(url, mode, out string applicationName))
				{
					Console.WriteLine($"  Parsed to: {applicationName}");
					if (applicationName == expected)
					{
						Console.WriteLine($"  ? PASS: Expected {expected}");
					}
					else
					{
						Console.WriteLine($"  ? FAIL: Expected {expected}, got {applicationName}");
						allTestsPassed = false;
					}
				}
				else
				{
					Console.WriteLine($"  ? FAIL: Could not parse URL");
					allTestsPassed = false;
				}
			}

			// Test edge cases
			Console.WriteLine("\n=== Testing Edge Cases ===");
			
			// Test SubdomainPreHyphens mode (should NOT apply PR transformation)
			if (ServiceFabricUrlResolver.TryGetApplicationNameFromUrl("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.SubdomainPreHyphens, out string preName))
			{
				Console.WriteLine($"SubdomainPreHyphens result: {preName}");
				if (preName == "guard")
				{
					Console.WriteLine("? PASS: SubdomainPreHyphens correctly extracts service name without PR transformation");
				}
				else
				{
					Console.WriteLine("? FAIL: SubdomainPreHyphens should extract 'guard'");
					allTestsPassed = false;
				}
			}

			// Test SubdomainPostHyphens mode (should NOT apply PR transformation)  
			if (ServiceFabricUrlResolver.TryGetApplicationNameFromUrl("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.SubdomainPostHyphens, out string postName))
			{
				Console.WriteLine($"SubdomainPostHyphens result: {postName}");
				if (postName == "12906")
				{
					Console.WriteLine("? PASS: SubdomainPostHyphens correctly extracts last part without PR transformation");
				}
				else
				{
					Console.WriteLine("? FAIL: SubdomainPostHyphens should extract '12906'");
					allTestsPassed = false;
				}
			}

			Console.WriteLine($"\n=== Test Results: {(allTestsPassed ? "ALL TESTS PASSED" : "SOME TESTS FAILED")} ===");
			
			if (!allTestsPassed)
			{
				Environment.Exit(1);
			}
		}
	}
}