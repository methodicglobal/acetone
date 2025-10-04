using System;
using System.Linq;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Standalone URL parsing functionality extracted from ServiceFabricUrlResolver
	/// This allows testing without Service Fabric SDK dependencies
	/// </summary>
	public static class StandaloneUrlParser
	{
		public enum ApplicationNameLocation
		{
			Subdomain,
			SubdomainPostHyphens,
			SubdomainPreHyphens,
			FirstUrlFragment
		}

		/// <summary>
		/// Returns the name of the service fabric application but using a simple algorithm based on the nameLocation mode
		/// </summary>
		/// <param name="url">original url which contains the application name</param>
		/// <param name="nameLocation">Dictates where the application name will be found. Subdomain (service.test.methodic.online), SubdomainWithEnvironment (test-service.methodic.online) or FirstUrlFragment (api.test.methodic.online/service)</param>
		/// <returns>name of application type to search for within the service fabric cluster</returns>
		public static bool TryGetApplicationNameFromUrl(string url, ApplicationNameLocation nameLocation, out string applicationName)
		{
			if (!url.StartsWith(Uri.UriSchemeHttp, StringComparison.InvariantCultureIgnoreCase) && !url.StartsWith(Uri.UriSchemeHttps, StringComparison.InvariantCultureIgnoreCase))
			{
				url = Uri.UriSchemeHttps + "://" + url;
			}
			if (Uri.TryCreate(url, UriKind.Absolute, out Uri originalUri))
			{
				string extractedName = string.Empty;
				
				switch (nameLocation)
				{
					case ApplicationNameLocation.Subdomain:
						extractedName = originalUri.Host.Split('.').First(); //expects service.uat.company.com
						break;
					case ApplicationNameLocation.SubdomainPreHyphens:
						extractedName = originalUri.Host.Split('.').First().Split('-').First(); //expects service-uat-01.company.com
						break;
					case ApplicationNameLocation.SubdomainPostHyphens:
						extractedName = originalUri.Host.Split('.').First().Split('-').Last(); //expects uat-01-service.company.com
						break;
					case ApplicationNameLocation.FirstUrlFragment:
						extractedName = originalUri.Segments.Length > 1 ? originalUri.Segments[1].Trim('/', '\\') : originalUri.AbsolutePath.Trim('/', '\\'); //expects connect.uat.copmany.com/service
						break;
					default:
						extractedName = originalUri.Segments[1].Trim('/', '\\');
						break;
				}

				// Check if this is a pull request URL pattern: {serviceName}-{pullRequestId}
				// Only check for Subdomain and FirstUrlFragment modes as they are most likely to contain PR patterns
				if ((nameLocation == ApplicationNameLocation.Subdomain || nameLocation == ApplicationNameLocation.FirstUrlFragment) && 
					extractedName.Contains("-") && 
					System.Text.RegularExpressions.Regex.IsMatch(extractedName, @"^(.+)-(\d+)$"))
				{
					var match = System.Text.RegularExpressions.Regex.Match(extractedName, @"^(.+)-(\d+)$");
					if (match.Success)
					{
						string serviceName = match.Groups[1].Value;
						string prNumber = match.Groups[2].Value;
						
						// Transform to Service Fabric application name format: {ServiceName}-PR{PullRequestId}
						// Capitalize first letter of service name to match typical Service Fabric naming conventions
						string capitalizedServiceName = char.ToUpper(serviceName[0]) + serviceName.Substring(1).ToLower();
						applicationName = $"{capitalizedServiceName}-PR{prNumber}";
						return true;
					}
				}

				// For non-PR URLs, return the extracted name as-is
				applicationName = extractedName;
				return true;
			}

			//Supplied URL has no resolvable application name
			applicationName = url;
			return false;
		}

		public static void RunTests()
		{
			Console.WriteLine("=== Testing Pull Request URL Parsing (Standalone) ===");
			
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
				
				if (TryGetApplicationNameFromUrl(url, mode, out string applicationName))
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
			if (TryGetApplicationNameFromUrl("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.SubdomainPreHyphens, out string preName))
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
			if (TryGetApplicationNameFromUrl("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.SubdomainPostHyphens, out string postName))
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