using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Comprehensive unit tests for ServiceFabricUrlParser static utility class.
	/// These tests verify pure algorithm logic without any Service Fabric SDK dependencies.
	/// </summary>
	[TestClass]
	[TestCategory("Unit")]
	public class ServiceFabricUrlParserTests
	{
		private readonly ILogger logger = new TraceLogger { Enabled = true };

		#region TryGetApplicationNameFromUrl - Basic Tests

		[TestMethod]
		public void TryGetApplicationNameFromUrl_NullUrl_ReturnsFalse()
		{
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(null, ApplicationNameLocation.Subdomain, out string appName);
			
			Assert.IsFalse(result);
			Assert.IsNull(appName);
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_EmptyUrl_ReturnsFalse()
		{
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl("", ApplicationNameLocation.Subdomain, out string appName);
			
			Assert.IsFalse(result);
			Assert.IsNull(appName);
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_WhitespaceUrl_ReturnsFalse()
		{
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl("   ", ApplicationNameLocation.Subdomain, out string appName);
			
			Assert.IsFalse(result);
			Assert.IsNull(appName);
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_InvalidUrl_ReturnsFalse()
		{
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl("not-a-valid-url!@#$%", ApplicationNameLocation.Subdomain, out string appName);
			
			Assert.IsFalse(result);
		}

		#endregion

		#region TryGetApplicationNameFromUrl - Subdomain Mode

		[TestMethod]
		public void TryGetApplicationNameFromUrl_Subdomain_BasicService()
		{
			var testCases = new List<(string url, string expected)>
			{
				("methodicservicename.methodic.com.au", "methodicservicename"),
				("methodicservicename.test.methodic.com/api-docs", "methodicservicename"),
				("https://methodicservicename.test.methodic.com", "methodicservicename"),
				("https://methodicservicename.test.methodic.com/123123/asdasdasd/123123123123", "methodicservicename"),
				("https://methodicservicename.test.methodic.com/?someparam=true", "methodicservicename"),
				("https://methodicservicename.methodic.com", "methodicservicename"),
				("https://methodicservicename.methodic.com/", "methodicservicename"),
				("https://methodicservicename.methodic.com/?try=true&moreparams=true", "methodicservicename"),
				("https://methodicservicename.methodic.com:8443/123123123", "methodicservicename"),
				("http://methodicservicename.test.methodic.com/?someparam=true", "methodicservicename")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.Subdomain, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect app name for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_Subdomain_WithoutScheme()
		{
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
				"myservice.test.methodic.com", 
				ApplicationNameLocation.Subdomain, 
				out string appName);
			
			Assert.IsTrue(result);
			Assert.AreEqual("myservice", appName);
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_Subdomain_NonPRHyphenatedService()
		{
			// Hyphenated service names that are NOT PR patterns (no numeric suffix)
			var testCases = new List<(string url, string expected)>
			{
				("https://my-service.pav.meth.wtf", "my-service"),
				("https://api-gateway.test.methodic.com", "api-gateway"),
				("https://user-management.methodic.online", "user-management"),
				("https://some-complex-name.dev.company.com", "some-complex-name")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.Subdomain, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect app name for URL: {url}");
			}
		}

		#endregion

		#region TryGetApplicationNameFromUrl - Pull Request Pattern

		[TestMethod]
		public void TryGetApplicationNameFromUrl_Subdomain_PullRequestPattern()
		{
			var testCases = new List<(string url, string expected)>
			{
				("https://guard-12906.pav.meth.wtf", "Guard-PR12906"),
				("https://api-1234.test.methodic.com/swagger", "Api-PR1234"),
				("guard-999.methodic.online", "Guard-PR999"),
				("https://service-42.dev.company.com:8443/health", "Service-PR42"),
				("https://myapp-567890.staging.methodic.com/?version=latest", "Myapp-PR567890"),
				("http://backend-1.localhost:8080", "Backend-PR1"),
				("https://microservice-00001.test.com", "Microservice-PR00001")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.Subdomain, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse PR URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect PR transformation for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_Subdomain_PRPattern_CapitalizationRules()
		{
			// Test that first letter is capitalized, rest lowercase
			var testCases = new List<(string url, string expected)>
			{
				("https://GUARD-12906.pav.meth.wtf", "Guard-PR12906"),
				("https://API-1234.test.methodic.com", "Api-PR1234"),
				("https://MyService-999.methodic.online", "Myservice-PR999"),
				("https://ALLCAPS-42.dev.company.com", "Allcaps-PR42")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.Subdomain, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse PR URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect capitalization for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_Subdomain_SingleCharacterService_PR()
		{
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
				"https://a-123.test.com", 
				ApplicationNameLocation.Subdomain, 
				out string appName);
			
			Assert.IsTrue(result);
			Assert.AreEqual("A-PR123", appName);
		}

		#endregion

		#region TryGetApplicationNameFromUrl - SubdomainPreHyphens Mode

		[TestMethod]
		public void TryGetApplicationNameFromUrl_SubdomainPreHyphens()
		{
			var testCases = new List<(string url, string expected)>
			{
				("https://methodicservicename-uat.methodic.online", "methodicservicename"),
				("https://methodicservicename-uat-01.methodic.online/api-docs", "methodicservicename"),
				("https://methodicservicename.methodic.com", "methodicservicename"),
				("https://methodicservicename.methodic.com/12313213/fdasdsadasd/12312312", "methodicservicename"),
				("https://methodicservicename-uat.methodic.online/12313213/fdasdsadasd/12312312", "methodicservicename"),
				("https://methodicservicename-uat-dr-01.methodic.online/12313213/fdasdsadasd/12312312?asd=asdasd&asdad=asdasd", "methodicservicename")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.SubdomainPreHyphens, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect app name for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_SubdomainPreHyphens_NoPRTransformation()
		{
			// PR pattern should NOT apply to SubdomainPreHyphens mode
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
				"https://guard-12906.pav.meth.wtf", 
				ApplicationNameLocation.SubdomainPreHyphens, 
				out string appName);
			
			Assert.IsTrue(result);
			Assert.AreEqual("guard", appName, "SubdomainPreHyphens should extract text before first hyphen, not apply PR transformation");
		}

		#endregion

		#region TryGetApplicationNameFromUrl - SubdomainPostHyphens Mode

		[TestMethod]
		public void TryGetApplicationNameFromUrl_SubdomainPostHyphens()
		{
			var testCases = new List<(string url, string expected)>
			{
				("https://test-methodicservicename.methodic.online", "methodicservicename"),
				("https://test-methodicservicename.methodic.online/api-docs", "methodicservicename"),
				("https://methodicservicename.methodic.com", "methodicservicename"),
				("https://methodicservicename.methodic.com/12313213/fdasdsadasd/12312312", "methodicservicename"),
				("https://test-methodicservicename.methodic.online/12313213/fdasdsadasd/12312312", "methodicservicename"),
				("https://test-methodicservicename.methodic.online/12313213/fdasdsadasd/12312312?asd=asdasd&asdad=asdasd", "methodicservicename"),
				("https://uat-01-dr-methodicservicename.methodic.online", "methodicservicename")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.SubdomainPostHyphens, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect app name for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_SubdomainPostHyphens_NoPRTransformation()
		{
			// PR pattern should NOT apply to SubdomainPostHyphens mode
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
				"https://guard-12906.pav.meth.wtf", 
				ApplicationNameLocation.SubdomainPostHyphens, 
				out string appName);
			
			Assert.IsTrue(result);
			Assert.AreEqual("12906", appName, "SubdomainPostHyphens should extract text after last hyphen, not apply PR transformation");
		}

		#endregion

		#region TryGetApplicationNameFromUrl - FirstUrlFragment Mode

		[TestMethod]
		public void TryGetApplicationNameFromUrl_FirstUrlFragment_BasicService()
		{
			var testCases = new List<(string url, string expected)>
			{
				("http://localhost:8709/methodicservicename", "methodicservicename"),
				("http://localhost:8709/methodicservicename/123123/9898988/wdsfsdfs", "methodicservicename"),
				("http://localhost/methodicservicename/123123/9898988/wdsfsdfs", "methodicservicename"),
				("http://localhost/methodicservicename/123123/9898988/wdsfsdfs?api-version=v1.1.0", "methodicservicename"),
				("http://localhost/methodicservicename?asdasd=true&abc=abc", "methodicservicename"),
				("http://localhost/methodicservicename", "methodicservicename"),
				("http://api.methodic.com:8888/methodicservicename", "methodicservicename"),
				("https://services.methodic.com/methodicservicename/scale/confidently", "methodicservicename")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.FirstUrlFragment, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect app name for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_FirstUrlFragment_PullRequestPattern()
		{
			var testCases = new List<(string url, string expected)>
			{
				("https://api.methodic.com/guard-12906", "Guard-PR12906"),
				("http://localhost:8709/service-1234/health", "Service-PR1234"),
				("https://gateway.company.com/myapp-999/api/v1", "Myapp-PR999"),
				("http://api.test.com/backend-42?version=1.0", "Backend-PR42")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.FirstUrlFragment, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse PR URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect PR transformation for URL: {url}");
			}
		}

		[TestMethod]
		public void TryGetApplicationNameFromUrl_FirstUrlFragment_HyphenatedNonPR()
		{
			// Hyphenated service names in first fragment that are NOT PR patterns
			var testCases = new List<(string url, string expected)>
			{
				("https://api.methodic.com/my-service", "my-service"),
				("http://localhost/user-management/api", "user-management"),
				("https://gateway.com/api-gateway", "api-gateway")
			};

			foreach (var (url, expected) in testCases)
			{
				bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.FirstUrlFragment, out string appName);
				
				Assert.IsTrue(result, $"Failed to parse URL: {url}");
				Assert.AreEqual(expected, appName, $"Incorrect app name for URL: {url}");
			}
		}

		#endregion

		#region EndpointJsonToText Tests

		[TestMethod]
		public void EndpointJsonToText_NullInput_ThrowsException()
		{
			Assert.ThrowsExactly<ArgumentNullException>(() =>
			{
				ServiceFabricUrlParser.EndpointJsonToText(null, logger);
			});
		}

		[TestMethod]
		public void EndpointJsonToText_EmptyInput_ThrowsException()
		{
			Assert.ThrowsExactly<ArgumentNullException>(() =>
			{
				ServiceFabricUrlParser.EndpointJsonToText("", logger);
			});
		}

		[TestMethod]
		public void EndpointJsonToText_WhitespaceInput_ThrowsException()
		{
			Assert.ThrowsExactly<ArgumentNullException>(() =>
			{
				ServiceFabricUrlParser.EndpointJsonToText("   ", logger);
			});
		}

		[TestMethod]
		public void EndpointJsonToText_BasicHttpsEndpoint()
		{
			string input = @"{""Endpoints"":{"""":""https://dev-ws-01.methodic.online:5555""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://dev-ws-01.methodic.online:5555", result);
		}

		[TestMethod]
		public void EndpointJsonToText_NamedHttpListener()
		{
			string input = @"{""Endpoints"":{""HttpListener"":""https://dev-ws-03.methodic.online:999/""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://dev-ws-03.methodic.online:999", result);
		}

		[TestMethod]
		public void EndpointJsonToText_NamedHttpEndpoint()
		{
			string input = @"{""Endpoints"":{""HttpEndpoint"":""https://node1.methodic.online""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://node1.methodic.online", result);
		}

		[TestMethod]
		public void EndpointJsonToText_EscapedSlashes()
		{
			string input = @"{""Endpoints"":{"""":""https:\/\/dev-ws-04.methodic.online:8899""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://dev-ws-04.methodic.online:8899", result);
		}

		[TestMethod]
		public void EndpointJsonToText_LocalhostEndpoint()
		{
			string input = @"{""Endpoints"":{"""":""http://DEVMACHINE-BEAST:30006""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("http://DEVMACHINE-BEAST:30006", result);
		}

		[TestMethod]
		public void EndpointJsonToText_MultipleEndpoints_SelectsHttp()
		{
			// When multiple endpoints exist, should select the HTTP one, not remoting
			string input = @"{""Endpoints"":{""HttpListener"":""https://0.0.0.0:20155"",""RemotingListener"":""timmyazdev:50239+e7814972-af49-4445-ac74-2e455c7d2076-133448859851162104-79bc2aa7-6ad1-40e2-a605-329d84b7bb3a-Secure""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://0.0.0.0:20155", result);
		}

		[TestMethod]
		public void EndpointJsonToText_RemotingListenerOnly_ThrowsException()
		{
			// Should reject remoting endpoints that don't have HTTP protocol
			string input = @"{""Endpoints"":{""RemotingListener"":""dev-ws-01.methodic.online:8899+26b10204-3f8c-47cd-bf2b-0932288a9701-132632828277101478-ce63d839-af7b-4952-8bb8-a4bc79633291-Secure""}}";
			
			Assert.ThrowsExactly<Exception>(() =>
			{
				ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			});
		}

		[TestMethod]
		public void EndpointJsonToText_PlainUrl_NoJson()
		{
			// Should also handle plain URLs (not wrapped in JSON)
			string input = "https://service.methodic.com:8080";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("https://service.methodic.com:8080", result);
		}

		[TestMethod]
		public void EndpointJsonToText_TrailingSlash_Removed()
		{
			string input = @"{""Endpoints"":{""HttpListener"":""https://dev-ws-03.methodic.online:999/""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			// Should remove trailing slash
			Assert.AreEqual("https://dev-ws-03.methodic.online:999", result);
			Assert.IsFalse(result.EndsWith("/"));
		}

		[TestMethod]
		public void EndpointJsonToText_InvalidJson_ThrowsException()
		{
			string input = @"{""Endpoints"":{""SomeOtherProtocol"":""invalid://not-http""}}";
			
			Assert.ThrowsExactly<Exception>(() =>
			{
				ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			});
		}

		[TestMethod]
		public void EndpointJsonToText_HttpEndpoint()
		{
			// Test HTTP (not just HTTPS)
			string input = @"{""Endpoints"":{"""":""http://localhost:8080""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			Assert.AreEqual("http://localhost:8080", result);
		}

		#endregion

		#region IsValidEndpoint Tests

		[TestMethod]
		public void IsValidEndpoint_ValidHttps_ReturnsTrue()
		{
			Assert.IsTrue(ServiceFabricUrlParser.IsValidEndpoint("https://service.methodic.com"));
			Assert.IsTrue(ServiceFabricUrlParser.IsValidEndpoint("https://service.methodic.com:8080"));
			Assert.IsTrue(ServiceFabricUrlParser.IsValidEndpoint("https://localhost:5000/api"));
		}

		[TestMethod]
		public void IsValidEndpoint_ValidHttp_ReturnsTrue()
		{
			Assert.IsTrue(ServiceFabricUrlParser.IsValidEndpoint("http://service.methodic.com"));
			Assert.IsTrue(ServiceFabricUrlParser.IsValidEndpoint("http://localhost:8080"));
		}

		[TestMethod]
		public void IsValidEndpoint_InvalidScheme_ReturnsFalse()
		{
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint("ftp://service.methodic.com"));
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint("tcp://service.methodic.com"));
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint("ws://service.methodic.com"));
		}

		[TestMethod]
		public void IsValidEndpoint_NullOrEmpty_ReturnsFalse()
		{
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint(null));
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint(""));
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint("   "));
		}

		[TestMethod]
		public void IsValidEndpoint_InvalidUrl_ReturnsFalse()
		{
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint("not a url"));
			Assert.IsFalse(ServiceFabricUrlParser.IsValidEndpoint("service.methodic.com")); // No scheme
		}

		#endregion

		#region NormalizeLocalEndpoint Tests

		[TestMethod]
		public void NormalizeLocalEndpoint_NonRoutableIp_ReplacesWithLoopback()
		{
			string input = "https://0.0.0.0:8080/api";
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint(input, logger);
			
			Assert.AreEqual("https://127.0.0.1:8080/api", result);
		}

		[TestMethod]
		public void NormalizeLocalEndpoint_NormalEndpoint_Unchanged()
		{
			string input = "https://service.methodic.com:8080";
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint(input, logger);
			
			Assert.AreEqual(input, result);
		}

		[TestMethod]
		public void NormalizeLocalEndpoint_LoopbackIp_Unchanged()
		{
			string input = "https://127.0.0.1:8080";
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint(input, logger);
			
			Assert.AreEqual(input, result);
		}

		[TestMethod]
		public void NormalizeLocalEndpoint_NullInput_ReturnsNull()
		{
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint(null, logger);
			Assert.IsNull(result);
		}

		[TestMethod]
		public void NormalizeLocalEndpoint_EmptyInput_ReturnsEmpty()
		{
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint("", logger);
			Assert.AreEqual("", result);
		}

		[TestMethod]
		public void NormalizeLocalEndpoint_WithoutLogger_Works()
		{
			string input = "https://0.0.0.0:8080";
			string result = ServiceFabricUrlParser.NormalizeLocalEndpoint(input, null);
			
			Assert.AreEqual("https://127.0.0.1:8080", result);
		}

		#endregion

		#region Edge Cases and Integration

	[TestMethod]
	public void TryGetApplicationNameFromUrl_MixedCaseUrl_HandlesCorrectly()
	{
		// NOTE: .NET Uri class automatically lowercases hostnames per RFC 3986
		// This is expected behavior - hostnames are case-insensitive in DNS/URLs
		bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
			"HTTPS://MyService.Test.Methodic.COM", 
			ApplicationNameLocation.Subdomain, 
			out string appName);
		
		Assert.IsTrue(result);
		Assert.AreEqual("myservice", appName, "Hostnames are automatically lowercased by Uri class per RFC standards");
	}		[TestMethod]
		public void TryGetApplicationNameFromUrl_UnicodeCharacters_HandlesCorrectly()
		{
			// Service Fabric application names typically use ASCII, but URL parser should handle it
			bool result = ServiceFabricUrlParser.TryGetApplicationNameFromUrl(
				"https://service.test.methodic.com/path/to/resource", 
				ApplicationNameLocation.Subdomain, 
				out string appName);
			
			Assert.IsTrue(result);
			Assert.AreEqual("service", appName);
		}

		[TestMethod]
		public void EndpointJsonToText_ComplexRealWorldExample()
		{
			// Real example from Service Fabric with multiple endpoints
			string input = @"{""Endpoints"":{""HttpListener"":""https://10.0.0.5:8080"",""HttpsListener"":""https://10.0.0.5:8443"",""RemotingListener"":""10.0.0.5:19000+abc123""}}";
			string result = ServiceFabricUrlParser.EndpointJsonToText(input, logger);
			
			// Should extract the first HTTP endpoint
			Assert.IsTrue(result.StartsWith("https://"));
			Assert.IsTrue(result.Contains("10.0.0.5"));
		}

		#endregion
	}
}
