using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	public class AcetoneUnitTests
	{

		[TestMethod]
		public void JsonEndpointExtraction()
		{
			var logger = new TraceLogger { Enabled = true };
			string addressObject = @"{""Endpoints"":{"""":""https://dev-ws-01.methodic.online:5555""}}";
			var endpoint = ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			Assert.AreEqual("https://dev-ws-01.methodic.online:5555", endpoint, true);

			addressObject = @"{""Endpoints"":{""HttpListener"":""https://dev-ws-03.methodic.online:999/""}}";
			endpoint = ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			Assert.AreEqual("https://dev-ws-03.methodic.online:999", endpoint, true);

			addressObject = @"{""Endpoints"":{""HttpEndpoint"":""https://node1.methodic.online""}}";
			endpoint = ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			Assert.AreEqual("https://node1.methodic.online", endpoint, true);

			addressObject = @"{""Endpoints"":{"""":""https:\/\/dev-ws-04.methodic.online:8899""}}";
			endpoint = ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			Assert.AreEqual("https://dev-ws-04.methodic.online:8899", endpoint, true);

			addressObject = @"{""Endpoints"":{"""":""http://DEVMACHINE-BEAST:30006""}}";
			endpoint = ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			Assert.AreEqual("http://DEVMACHINE-BEAST:30006", endpoint, true);

			addressObject = @"{""Endpoints"":{""RemotingListener"":""dev-ws-01.methodic.online:8899+26b10204-3f8c-47cd-bf2b-0932288a9701-132632828277101478-ce63d839-af7b-4952-8bb8-a4bc79633291-Secure""}}";
			_ = Assert.ThrowsExactly<System.Exception>(() =>
			{
				return ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			});
			addressObject = @"	{""Endpoints"":{""HttpListener"":""https:\/\/0.0.0.0:20155"",""RemotingListener"":""timmyazdev:50239+e7814972-af49-4445-ac74-2e455c7d2076-133448859851162104-79bc2aa7-6ad1-40e2-a605-329d84b7bb3a-Secure""}}";
			endpoint = ServiceFabricUrlResolver.EndpointJsonToText(addressObject, logger);
			Assert.AreEqual("https://0.0.0.0:20155", endpoint, true);
		}


		[TestMethod]
		public void ApplicationNameResolution()
		{
			var serviceName = "methodicservicename";
			List<string> subdomainTests = new List<string>
			{
				$"{serviceName}.methodic.com.au",
				$"{serviceName}.test.methodic.com/api-docs",
				$"https://{serviceName}.test.methodic.com",
				$"https://{serviceName}.test.methodic.com/123123/asdasdasd/123123123123",
				$"https://{serviceName}.test.methodic.com/?someparam=true",
				$"https://{serviceName}.methodic.com",
				$"https://{serviceName}.methodic.com/",
				$"https://{serviceName}.methodic.com/?try=true&moreparams=true",
				$"https://{serviceName}.methodic.com:8443/123123123",
				$"http://{serviceName}.test.methodic.com/?someparam=true",
				$"{serviceName}.test.methodic.com",
				$"{serviceName}.test.methodic.com/?someparam=true"
			};
			foreach (string testUrl in subdomainTests)
			{
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(testUrl, ApplicationNameLocation.Subdomain, out string applicationName))
				{
					Assert.Fail("Could not resolve application name from URL");
				}
				Assert.AreEqual(serviceName, applicationName);
			}

			List<string> subdomainPreHyphenTests = new List<string>
			{
				$"https://{serviceName}-uat.methodic.online",
				$"https://{serviceName}-uat-01.methodic.online/api-docs",
				$"https://{serviceName}.methodic.com",
				$"https://{serviceName}.methodic.com/12313213/fdasdsadasd/12312312",
				$"https://{serviceName}-uat.methodic.online/12313213/fdasdsadasd/12312312",
				$"https://{serviceName}-uat-dr-01.methodic.online/12313213/fdasdsadasd/12312312?asd=asdasd&asdad=asdasd",
			};
			foreach (string testUrl in subdomainPreHyphenTests)
			{
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(testUrl, ApplicationNameLocation.SubdomainPreHyphens, out string applicationName))
				{
					Assert.Fail("Could not resolve application name from URL");
				}
				Assert.AreEqual(serviceName, applicationName);

			}

			List<string> subdomainPostHyphenTests = new List<string>
			{
				$"https://test-{serviceName}.methodic.online",
				$"https://test-{serviceName}.methodic.online/api-docs",
				$"https://{serviceName}.methodic.com",
				$"https://{serviceName}.methodic.com/12313213/fdasdsadasd/12312312",
				$"https://test-{serviceName}.methodic.online/12313213/fdasdsadasd/12312312",
				$"https://test-{serviceName}.methodic.online/12313213/fdasdsadasd/12312312?asd=asdasd&asdad=asdasd",
			};
			foreach (string testUrl in subdomainPostHyphenTests)
			{
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(testUrl, ApplicationNameLocation.SubdomainPostHyphens, out string applicationName))
				{
					Assert.Fail("Could not resolve application name from URL");
				}
				Assert.AreEqual(serviceName, applicationName);

			}

			List<string> firstUrlFragmentTests = new List<string>
			{
				$"http://localhost:8709/{serviceName}",
				$"http://localhost:8709/{serviceName}/123123/9898988/wdsfsdfs",
				$"http://localhost/{serviceName}/123123/9898988/wdsfsdfs",
				$"http://localhost/{serviceName}/123123/9898988/wdsfsdfs?api-version=v1.1.0",
				$"http://localhost/{serviceName}?asdasd=true&abc=abc",
				$"http://localhost/{serviceName}",
				$"http://api.methodic.com:8888/{serviceName}",
				$"https://services.methodic.com/{serviceName}/scale/confidently"
			};
			foreach (string testUrl in firstUrlFragmentTests)
			{
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(testUrl, ApplicationNameLocation.FirstUrlFragment, out string applicationName))
				{
					Assert.Fail("Could not resolve application name from URL");
				}
				Assert.AreEqual(serviceName, applicationName);
			}
		}

		[TestMethod]
		public void PullRequestApplicationNameResolution()
		{
			// Test subdomain mode with pull request URLs
			List<(string url, string expectedAppName)> subdomainPRTests = new List<(string, string)>
			{
				("https://guard-12906.pav.meth.wtf", "Guard-PR12906"),
				("https://api-1234.test.methodic.com/swagger", "Api-PR1234"),
				("guard-999.methodic.online", "Guard-PR999"),
				("https://service-42.dev.company.com:8443/health", "Service-PR42"),
				("https://myapp-567890.staging.methodic.com/?version=latest", "Myapp-PR567890")
			};

			foreach (var (url, expectedAppName) in subdomainPRTests)
			{
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.Subdomain, out string applicationName))
				{
					Assert.Fail($"Could not resolve application name from PR URL: {url}");
				}
				Assert.AreEqual(expectedAppName, applicationName, $"Failed for URL: {url}");
			}

			// Test first URL fragment mode with pull request URLs
			List<(string url, string expectedAppName)> firstFragmentPRTests = new List<(string, string)>
			{
				("https://api.methodic.com/guard-12906", "Guard-PR12906"),
				("http://localhost:8709/service-1234/health", "Service-PR1234"),
				("https://gateway.company.com/myapp-999/api/v1", "Myapp-PR999")
			};

			foreach (var (url, expectedAppName) in firstFragmentPRTests)
			{
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(url, ApplicationNameLocation.FirstUrlFragment, out string applicationName))
				{
					Assert.Fail($"Could not resolve application name from PR URL: {url}");
				}
				Assert.AreEqual(expectedAppName, applicationName, $"Failed for URL: {url}");
			}

			// Test that regular URLs (without PR pattern) still work correctly
			List<(string url, string expectedAppName)> regularTests = new List<(string, string)>
			{
				("https://guard.pav.meth.wtf", "guard"),
				("https://my-service.methodic.com", "my-service"), // This has hyphen but no number, so not a PR
				("https://api.methodic.com/myservice", "myservice")
			};

			foreach (var (url, expectedAppName) in regularTests)
			{
				ApplicationNameLocation mode = url.Contains("/myservice") ? ApplicationNameLocation.FirstUrlFragment : ApplicationNameLocation.Subdomain;
				if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(url, mode, out string applicationName))
				{
					Assert.Fail($"Could not resolve application name from regular URL: {url}");
				}
				Assert.AreEqual(expectedAppName, applicationName, $"Failed for regular URL: {url}");
			}
		}
	}
}
