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
			Assert.ThrowsException<System.Exception>(() =>
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
	}
}
