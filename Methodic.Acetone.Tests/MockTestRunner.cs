using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Simple console test for the mock Service Fabric implementation
	/// </summary>
	public class MockTestRunner
	{
		public static async Task<bool> RunTests()
		{
			var logger = new TraceLogger { Enabled = true };
			bool allTestsPassed = true;

			Console.WriteLine("=== Running Mock Service Fabric Tests ===");

			try
			{
				// Test 1: Basic service resolution
				Console.WriteLine("\nTest 1: Basic Service Resolution");
				using (var resolver = new TestableServiceFabricUrlResolver(logger, "LOCALHOST:19000"))
				{
					var serviceUri = await resolver.ResolveServiceUri("ServiceA", Guid.NewGuid());
					Console.WriteLine($"ServiceA resolved to: {serviceUri}");
					
					if (serviceUri.Contains("servicea.pav.meth.wtf"))
					{
						Console.WriteLine("? PASS: Regular service resolution works");
					}
					else
					{
						Console.WriteLine("? FAIL: Regular service resolution failed");
						allTestsPassed = false;
					}
				}

				// Test 2: PR service resolution
				Console.WriteLine("\nTest 2: PR Service Resolution");
				using (var resolver = new TestableServiceFabricUrlResolver(logger, "LOCALHOST:19000"))
				{
					var prServiceUri = await resolver.ResolveServiceUri("ServiceA-PR1234", Guid.NewGuid());
					Console.WriteLine($"ServiceA-PR1234 resolved to: {prServiceUri}");
					
					if (prServiceUri.Contains("servicea-1234.pav.meth.wtf"))
					{
						Console.WriteLine("? PASS: PR service resolution works");
					}
					else
					{
						Console.WriteLine("? FAIL: PR service resolution failed");
						allTestsPassed = false;
					}
				}

				// Test 3: Multiple services
				Console.WriteLine("\nTest 3: Multiple Service Resolution");
				using (var resolver = new TestableServiceFabricUrlResolver(logger, "LOCALHOST:19000"))
				{
					for (char service = 'A'; service <= 'D'; service++)
					{
						string serviceName = $"Service{service}";
						var serviceUri = await resolver.ResolveServiceUri(serviceName, Guid.NewGuid());
						Console.WriteLine($"{serviceName} resolved to: {serviceUri}");
						
						string expectedEndpoint = $"service{service.ToString().ToLower()}.pav.meth.wtf";
						if (serviceUri.Contains(expectedEndpoint))
						{
							Console.WriteLine($"? PASS: {serviceName} resolution works");
						}
						else
						{
							Console.WriteLine($"? FAIL: {serviceName} resolution failed");
							allTestsPassed = false;
						}
					}
				}

				// Test 4: URL parsing with PR pattern
				Console.WriteLine("\nTest 4: URL Parsing with PR Pattern");
				if (ServiceFabricUrlResolver.TryGetApplicationNameFromUrl("https://guard-12906.pav.meth.wtf", ApplicationNameLocation.Subdomain, out string appName))
				{
					Console.WriteLine($"URL parsed to application name: {appName}");
					if (appName == "Guard-PR12906")
					{
						Console.WriteLine("? PASS: PR URL parsing works");
					}
					else
					{
						Console.WriteLine("? FAIL: PR URL parsing failed");
						allTestsPassed = false;
					}
				}
				else
				{
					Console.WriteLine("? FAIL: Could not parse PR URL");
					allTestsPassed = false;
				}

				// Test 5: Error handling for non-existent service
				Console.WriteLine("\nTest 5: Error Handling");
				using (var resolver = new TestableServiceFabricUrlResolver(logger, "LOCALHOST:19000"))
				{
					try
					{
						await resolver.ResolveServiceUri("NonExistentService", Guid.NewGuid());
						Console.WriteLine("? FAIL: Should have thrown exception for non-existent service");
						allTestsPassed = false;
					}
					catch (KeyNotFoundException ex)
					{
						Console.WriteLine($"Expected exception caught: {ex.Message}");
						Console.WriteLine("? PASS: Error handling works");
					}
				}

			}
			catch (Exception ex)
			{
				Console.WriteLine($"? FAIL: Unexpected exception: {ex.Message}");
				Console.WriteLine($"Stack trace: {ex.StackTrace}");
				allTestsPassed = false;
			}

			Console.WriteLine($"\n=== Test Results: {(allTestsPassed ? "ALL TESTS PASSED" : "SOME TESTS FAILED")} ===");
			return allTestsPassed;
		}
	}
}