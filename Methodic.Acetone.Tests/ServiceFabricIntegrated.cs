using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Web.Iis.Rewrite;

namespace Methodic.Acetone.Tests
{
	[TestClass]
	public class ServiceFabricIntegrated
	{
		readonly ILogger logger = new TraceLogger{ Enabled = true };


		//TODO: Move these to settings?
		private const string ClusterEndpoint = "LOCALHOST:19000";

		private static readonly List<string> services = new List<string> { "ServiceA", "ServiceB" };

		//Single place to update security/connections
		private static ServiceFabricUrlResolver CreateResolver(ILogger instanceLogger)
		{
			return new ServiceFabricUrlResolver(instanceLogger, ClusterEndpoint);
			//return new ServiceFabricUrlResolver(instanceLogger, ClusterEndpoint, "<creds_cert_thumbprint>", "<cluster_cert_thumbprint>");
		}



		[TestMethod]
		public void EndpointResolutionSuccess()
		{
			int numberOfTests = 100000;
			Random random = new Random();
			ConcurrentBag<long> times = new ConcurrentBag<long>();
			
			using (var resolver = CreateResolver(logger))
			{
				_ = Parallel.For(0, numberOfTests, x =>
				  {
					  int serviceIndex = random.Next(0, services.Count - 1);
					  var sw = Stopwatch.StartNew();
					  resolver.ResolveServiceUri(services[serviceIndex], Guid.Empty).Wait();
					  sw.Stop();
					  times.Add(sw.ElapsedMilliseconds);
				  });
			}
			double averageTime = times.Average();
			Console.WriteLine($"Cached {ServiceFabricUrlResolver.CachedApplicationCount} applications and {ServiceFabricUrlResolver.CachedServicesCount} services");
			Console.WriteLine($"Average time for ResolveServiceUri is {averageTime} milliseconds across {times.Count} measured calls");
			Assert.IsTrue(averageTime < 100, $"Expected ResolveServiceUri to average under 100 milliseconds per call over {times.Count} calls but was {averageTime} instead");
		}

		[TestMethod]
		public async Task SingleEndpointResolution()
		{
			using (var resolver = CreateResolver(logger))
			{
				Random random = new Random();
				int serviceIndex = random.Next(0, services.Count - 1);
				Guid invocationId = Guid.NewGuid();
				var serviceUri = await resolver.ResolveServiceUri(services[serviceIndex], invocationId);
				Assert.IsTrue(serviceUri.ToUpperInvariant().Contains(Environment.MachineName.ToUpperInvariant()) || serviceUri.Contains("127.0.0.1"));
			}
		}

		[TestMethod]
		public async Task SecureEndpointResolution()
		{
			using (var resolver = CreateResolver(logger))
			{
				Random random = new Random();
				int serviceIndex = random.Next(0, services.Count - 1);
				Guid invocationId = Guid.NewGuid();
				var serviceUri = await resolver.ResolveServiceUri(services[serviceIndex], invocationId);
				Assert.IsTrue(serviceUri.ToUpperInvariant().Contains(Environment.MachineName.ToUpperInvariant()) || serviceUri.Contains("127.0.0.1"));
			}
		}

		[TestMethod]
		public async Task CachedEndpointResolutionTest()
		{
			var resolvedUrls = new List<string>();
			using (var resolver = CreateResolver(logger))
			{
				Guid invocationId = Guid.NewGuid();
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.First(), invocationId));
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.First(), invocationId));
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.Last(), invocationId));
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.First(), invocationId));
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.First(), invocationId));
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.Last(), invocationId));
				resolvedUrls.Add(await resolver.ResolveServiceUri(services.Last(), invocationId));
			}
			Assert.AreEqual<int>(2, ServiceFabricUrlResolver.CachedApplicationCount, $"Expected storage and rules to be cached, but have {ServiceFabricUrlResolver.CachedApplicationCount} cached items. URLs resolved:  {string.Join(", ", resolvedUrls)})");
		}

		[TestMethod]
		public void CheckInitialization()
		{
			//Setup
			var rewriter = new ServiceFabricLocator(logger);
			var context = new FakeRewriteContext { RewriteCacheEnabled = false };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", ClusterEndpoint },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
				//{"ServerCertificateThumbprints", "<server_cert_thumbprint>" },
				//{"ClientCertificateThumbprint", "<client_cert_thumbprint>" }
			};

			//Run
			rewriter.Initialize(settings, context);

			//Assert
			Assert.IsTrue(rewriter.ClusterConnectionStrings?.Contains(ClusterEndpoint));
			Assert.AreEqual(rewriter.ApplicationNameLocation, ApplicationNameLocation.Subdomain);
			Assert.IsFalse(rewriter.RewriteContext.RewriteCacheEnabled);
		}

		[TestMethod]
		public void CheckRewriteWithCommonNames()
		{
			string serviceName = services.First();
			string url = $"https://{serviceName}.methodic.online";
			var rewriter = new ServiceFabricLocator(logger);
			var context = new FakeRewriteContext { RewriteCacheEnabled = true };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", ClusterEndpoint },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "CertificateCommonName" },
				{"ClientCertificateSubjectDistinguishedName", "E=info@methodic.com, CN=Methodic Global, CN=Users, DC=methodic, DC=online" },
				{"ClientCertificateIssuerDistinguishedName", "CN=Methodic-Test-Certificate-Authority, DC=methodic, DC=online" },
				{ "ServerCertificateCommonNames", "CN=*.methodic.online" }
			};
			rewriter.Initialize(settings, context);
			var redirectUrl = rewriter.Rewrite(url);
			Assert.IsNotNull(redirectUrl);
		}

		[TestMethod]
		public void CheckFinalRewrite()
		{
			string serviceName = services.First();
			string url = $"https://{serviceName}.methodic.online";
			var rewriter = new ServiceFabricLocator(logger);
			var context = new FakeRewriteContext { RewriteCacheEnabled = true };
			var settings = new Dictionary<string, string>
			{
				{"ClusterConnectionStrings", ClusterEndpoint },
				{"ApplicationNameLocation", "Subdomain" },
				{"EnableLogging", "true" },
				{"CredentialsType", "Local" }
				//{"ServerCertificateThumbprints", "<server_cert_thumbprint>" },
				//{"ClientCertificateThumbprint", "<client_certcreds_thumbprint>" }
			};
			rewriter.Initialize(settings, context);
			var redirectUrl = rewriter.Rewrite(url);
			Assert.IsNotNull(redirectUrl);
		}

		[TestMethod]
		public void CheckPullRequestUrlExtraction()
		{
			// Test that pull request URLs are correctly parsed
			string prUrl = "https://guard-12906.pav.meth.wtf";
			
			if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(prUrl, ApplicationNameLocation.Subdomain, out string applicationName))
			{
				Assert.Fail("Could not resolve application name from PR URL");
			}
			
			Assert.AreEqual("Guard-PR12906", applicationName, "Pull request URL should be transformed to Guard-PR12906");
			
			// Test that normal URLs still work
			string normalUrl = "https://guard.pav.meth.wtf";
			
			if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(normalUrl, ApplicationNameLocation.Subdomain, out string normalAppName))
			{
				Assert.Fail("Could not resolve application name from normal URL");
			}
			
			Assert.AreEqual("guard", normalAppName, "Normal URL should extract service name as-is");
		}

		public class FakeRewriteContext : IRewriteContext
		{
			public bool RewriteCacheEnabled { get; set; }

			public void ClearRewriteCache()
			{
				throw new NotImplementedException();
			}
		}
	}
}
