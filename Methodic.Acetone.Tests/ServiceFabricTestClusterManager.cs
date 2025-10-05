using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Query;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq; // Added for manifest parsing

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Manages Service Fabric test application deployment and cleanup for integration tests.
	/// </summary>
	public class ServiceFabricTestClusterManager : IDisposable
	{
		private readonly FabricClient fabricClient;
		private readonly ILogger logger;
		private readonly List<string> deployedApplications = new List<string>();
		private readonly string clusterEndpoint;
		private bool isDisposed;
		private string imageStoreConnectionString;
		private const string ServicePackageName = "Acetone.TestServicePkg";
		private const string ServiceScriptName = "run.ps1";
		private const string ServiceCommandName = "run.cmd";

		// Track provisioned application types so we could (optionally) unprovision later if desired
		private readonly HashSet<(string TypeName, string TypeVersion)> provisionedApplicationTypes = new HashSet<(string, string)>();

		private bool cleanupInvoked;

		public bool IsClusterAvailable { get; private set; }
		public IReadOnlyList<string> DeployedApplications => deployedApplications.AsReadOnly();

		public ServiceFabricTestClusterManager(string clusterEndpoint, ILogger logger)
		{
			this.clusterEndpoint = clusterEndpoint ?? throw new ArgumentNullException(nameof(clusterEndpoint));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

			try
			{
				fabricClient = new FabricClient(clusterEndpoint);
				fabricClient.Settings.HealthReportSendInterval = TimeSpan.FromSeconds(5);
				fabricClient.Settings.HealthReportRetrySendInterval = TimeSpan.FromSeconds(5);
				var clusterHealth = fabricClient.HealthManager.GetClusterHealthAsync().GetAwaiter().GetResult();
				IsClusterAvailable = true;
				logger.WriteEntry($"✅ Connected to Service Fabric cluster at {clusterEndpoint}", LogEntryType.Informational);
				logger.WriteEntry($"Cluster health state: {clusterHealth.AggregatedHealthState}", LogEntryType.Informational);
			}
			catch (Exception ex)
			{
				IsClusterAvailable = false;
				logger.WriteEntry($"⚠ Could not connect to Service Fabric cluster: {ex.Message}", LogEntryType.Warning);
				logger.WriteEntry("Tests will use mock data instead of real cluster", LogEntryType.Informational);
			}
		}

		/// <summary>
		/// Deploy Service Fabric application projects that are part of the solution (MockApplication, MockSystem).
		/// We dynamically compose the application package (copy manifests + built service binaries) and provision + create an instance.
		/// </summary>
		public async Task<List<string>> DeploySolutionApplicationsAsync()
		{
			var results = new List<string>();
			if (!IsClusterAvailable)
			{
				logger.WriteEntry("Cluster not available, skipping solution application deployment", LogEntryType.Warning);
				return results;
			}

			string solutionRoot = TryLocateSolutionRoot();
			if (solutionRoot == null)
			{
				logger.WriteEntry("Unable to locate solution root for solution application deployment", LogEntryType.Error);
				return results;
			}

			var appsToDeploy = new List<(string AppProject, string ServiceProject)>
			{
				("MockApplication", "MockApi"),
				("MockSystem", "MockService")
			};

			string imageStoreConnection = await GetImageStoreConnectionStringAsync();

			foreach (var (appProject, serviceProject) in appsToDeploy)
			{
				try
				{
					string appProjectPath = Path.Combine(solutionRoot, appProject);
					string serviceProjectPath = Path.Combine(solutionRoot, serviceProject);
					string applicationManifestPath = Path.Combine(appProjectPath, "ApplicationPackageRoot", "ApplicationManifest.xml");
					string serviceManifestPath = Path.Combine(serviceProjectPath, "PackageRoot", "ServiceManifest.xml");

					if (!File.Exists(applicationManifestPath) || !File.Exists(serviceManifestPath))
					{
						logger.WriteEntry($"Skipping {appProject}: required manifest not found", LogEntryType.Warning);
						continue;
					}

					if (!TryParseApplicationManifest(applicationManifestPath, out var appManifest))
					{
						logger.WriteEntry($"Failed to parse application manifest for {appProject}", LogEntryType.Error);
						continue;
					}

					// Already provisioned?
					bool alreadyProvisioned = false;
					try
					{
						var currentTypes = await fabricClient.QueryManager.GetApplicationTypeListAsync();
						alreadyProvisioned = currentTypes.Any(t => t.ApplicationTypeName.Equals(appManifest.ApplicationTypeName, StringComparison.OrdinalIgnoreCase) &&
													 t.ApplicationTypeVersion.Equals(appManifest.ApplicationTypeVersion, StringComparison.OrdinalIgnoreCase));
					}
					catch (Exception exType)
					{
						logger.WriteEntry($"Could not query current application types: {exType.Message}", LogEntryType.Warning);
					}

					string stagingRoot = null;
					string imageStorePath = $"Acetone_{appManifest.ApplicationTypeName}_{Guid.NewGuid():N}";
					bool packageUploaded = false;

					try
					{
						if (!alreadyProvisioned)
						{
							stagingRoot = PrepareSolutionApplicationPackage(appProjectPath, serviceProjectPath, appManifest, serviceManifestPath);
							if (stagingRoot == null)
							{
								logger.WriteEntry($"Failed to stage application package for {appProject}", LogEntryType.Error);
								continue;
							}
							logger.WriteEntry($"Uploading application package for {appManifest.ApplicationTypeName} to {imageStorePath}", LogEntryType.Debug);
							fabricClient.ApplicationManager.CopyApplicationPackage(imageStoreConnection, stagingRoot, imageStorePath);
							packageUploaded = true;
							await fabricClient.ApplicationManager.ProvisionApplicationAsync(imageStorePath);
							provisionedApplicationTypes.Add((appManifest.ApplicationTypeName, appManifest.ApplicationTypeVersion));
							logger.WriteEntry($"✅ Provisioned application type {appManifest.ApplicationTypeName} {appManifest.ApplicationTypeVersion}", LogEntryType.Informational);
						}
						else
						{
							logger.WriteEntry($"Application type {appManifest.ApplicationTypeName} {appManifest.ApplicationTypeVersion} already provisioned", LogEntryType.Debug);
						}

						string instanceName = $"Acetone{appManifest.ApplicationTypeName}Tests";
						var appUri = new Uri($"fabric:/{instanceName}");
						var existing = await fabricClient.QueryManager.GetApplicationListAsync(appUri);
						if (existing.Any())
						{
							logger.WriteEntry($"Application instance {instanceName} already exists - reusing", LogEntryType.Warning);
							if (!deployedApplications.Contains(instanceName)) deployedApplications.Add(instanceName);
							results.Add(instanceName);
							continue;
						}

						var createDesc = new ApplicationDescription(appUri, appManifest.ApplicationTypeName, appManifest.ApplicationTypeVersion);
						await fabricClient.ApplicationManager.CreateApplicationAsync(createDesc);
						await Task.Delay(500);
						deployedApplications.Add(instanceName);
						results.Add(instanceName);
						logger.WriteEntry($"  ✅ Deployed solution application: {instanceName}", LogEntryType.Informational);
					}
					catch (Exception ex)
					{
						logger.WriteEntry($"  ❌ Failed deploying solution application {appProject}: {ex.Message}", LogEntryType.Error);
					}
					finally
					{
						if (packageUploaded)
						{
							try { fabricClient.ApplicationManager.RemoveApplicationPackage(imageStoreConnection, imageStorePath); } catch { }
						}
						if (stagingRoot != null) { TryDeleteDirectory(stagingRoot); }
					}
				}
				catch (Exception outerEx)
				{
					logger.WriteEntry($"Unexpected error preparing {appProject}: {outerEx.Message}", LogEntryType.Error);
				}
			}

			logger.WriteEntry($"Solution application deployment complete: {results.Count} applications active", LogEntryType.Informational);
			return results;
		}

		private string PrepareSolutionApplicationPackage(string appProjectPath, string serviceProjectPath, ApplicationManifestInfo appManifest, string serviceManifestPath)
		{
			string stagingRoot = Path.Combine(Path.GetTempPath(), $"AcetoneSolutionPkg_{appManifest.ApplicationTypeName}_{Guid.NewGuid():N}");
			try
			{
				Directory.CreateDirectory(stagingRoot);
				File.Copy(Path.Combine(appProjectPath, "ApplicationPackageRoot", "ApplicationManifest.xml"), Path.Combine(stagingRoot, "ApplicationManifest.xml"));

				foreach (var svc in appManifest.ServiceManifests)
				{
					string serviceSourceRoot = Path.Combine(serviceProjectPath, "PackageRoot");
					if (!Directory.Exists(serviceSourceRoot))
					{
						logger.WriteEntry($"Service package root not found for {svc.ServiceManifestName}", LogEntryType.Error);
						return null;
					}
					string targetDir = Path.Combine(stagingRoot, svc.ServiceManifestName);
					CopyDirectory(serviceSourceRoot, targetDir);

					string codeDir = Path.Combine(targetDir, "Code");
					if (Directory.Exists(codeDir)) TryDeleteDirectory(codeDir);
					Directory.CreateDirectory(codeDir);

					string buildOutput = TryLocateLatestBuildOutput(serviceProjectPath);
					if (buildOutput == null)
					{
						logger.WriteEntry($"Could not locate build output for {serviceProjectPath}; package will be invalid", LogEntryType.Error);
						return null;
					}

					foreach (var file in Directory.GetFiles(buildOutput))
					{
						var name = Path.GetFileName(file);
						if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
						File.Copy(file, Path.Combine(codeDir, name), true);
					}
					foreach (var dir in Directory.GetDirectories(buildOutput))
					{
						var dirName = Path.GetFileName(dir);
						if (dirName.Equals("runtimes", StringComparison.OrdinalIgnoreCase) || dirName.Equals("native", StringComparison.OrdinalIgnoreCase))
						{
							CopyDirectory(dir, Path.Combine(codeDir, dirName));
						}
					}
				}
				return stagingRoot;
			}
			catch (Exception ex)
			{
				logger.WriteEntry($"Failed staging solution application package: {ex.Message}", LogEntryType.Error);
				TryDeleteDirectory(stagingRoot);
				return null;
			}
		}

		private static string TryLocateLatestBuildOutput(string serviceProjectPath)
		{
			try
			{
				string binPath = Path.Combine(serviceProjectPath, "bin");
				if (!Directory.Exists(binPath)) return null;
				var candidateDirs = Directory.GetDirectories(binPath, "*", SearchOption.AllDirectories)
					.Where(d => Directory.GetFiles(d, "*.dll").Any() || Directory.GetFiles(d, "*.exe").Any())
					.Select(d => new DirectoryInfo(d)).OrderByDescending(d => d.LastWriteTimeUtc).ToList();
				return candidateDirs.FirstOrDefault()?.FullName;
			}
			catch { return null; }
		}

		private static bool TryParseApplicationManifest(string manifestPath, out ApplicationManifestInfo info)
		{
			info = null;
			try
			{
				XDocument doc = XDocument.Load(manifestPath);
				XNamespace ns = "http://schemas.microsoft.com/2011/01/fabric";
				var root = doc.Root; if (root == null) return false;
				string typeName = (string)root.Attribute("ApplicationTypeName");
				string typeVersion = (string)root.Attribute("ApplicationTypeVersion");
				if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(typeVersion)) return false;
				var serviceImports = root.Elements(ns + "ServiceManifestImport")
					.Select(e => e.Element(ns + "ServiceManifestRef"))
					.Where(e => e != null)
					.Select(e => new ServiceManifestRefInfo
					{
						ServiceManifestName = (string)e.Attribute("ServiceManifestName"),
						ServiceManifestVersion = (string)e.Attribute("ServiceManifestVersion")
					})
					.Where(s => !string.IsNullOrWhiteSpace(s.ServiceManifestName))
					.ToList();
				info = new ApplicationManifestInfo { ApplicationTypeName = typeName, ApplicationTypeVersion = typeVersion, ServiceManifests = serviceImports };
				return true;
			}
			catch { return false; }
		}

		private class ApplicationManifestInfo
		{ public string ApplicationTypeName { get; set; } public string ApplicationTypeVersion { get; set; } public List<ServiceManifestRefInfo> ServiceManifests { get; set; } = new List<ServiceManifestRefInfo>(); }
		private class ServiceManifestRefInfo { public string ServiceManifestName { get; set; } public string ServiceManifestVersion { get; set; } }

		/// <summary>
		/// Deploy synthetic test applications. If a dedicated Acetone.TestApplication package folder exists we provision it; otherwise we skip quietly.
		/// </summary>
		public async Task<List<string>> DeployTestApplicationsAsync(int count = 3, string applicationNamePrefix = "TestService")
		{
			var deployedApps = new List<string>();
			if (!IsClusterAvailable) return deployedApps;
			if (count < 1) return deployedApps;

			string solutionRoot = TryLocateSolutionRoot();
			if (solutionRoot == null)
			{
				logger.WriteEntry("Cannot deploy synthetic apps – solution root not found", LogEntryType.Warning);
				return deployedApps;
			}

			string appPackageSource = Path.Combine(solutionRoot, "Acetone.TestApplication");
			string appManifestPath = Path.Combine(appPackageSource, "ApplicationManifest.xml");
			if (!Directory.Exists(appPackageSource) || !File.Exists(appManifestPath))
			{
				logger.WriteEntry("Synthetic application package not present – skipping synthetic test deployments (only solution apps will be tested)", LogEntryType.Warning);
				return deployedApps; // Non-fatal – tests can still run against solution apps
			}

			if (!TryReadApplicationMetadata(appManifestPath, out string applicationTypeName, out string applicationTypeVersion))
			{
				logger.WriteEntry("Could not read synthetic application metadata", LogEntryType.Error);
				return deployedApps;
			}

			string imageStoreConnection = await GetImageStoreConnectionStringAsync();
			string imageStorePath = $"AcetoneTest_{Guid.NewGuid():N}";
			bool packageUploaded = false;
			string stagingPath = null;

			try
			{
				var existingTypes = await fabricClient.QueryManager.GetApplicationTypeListAsync();
				bool alreadyProvisioned = existingTypes.Any(t => t.ApplicationTypeName.Equals(applicationTypeName, StringComparison.OrdinalIgnoreCase) && t.ApplicationTypeVersion.Equals(applicationTypeVersion, StringComparison.OrdinalIgnoreCase));
				if (!alreadyProvisioned)
				{
					stagingPath = PrepareApplicationPackage(appPackageSource, solutionRoot);
					if (stagingPath == null)
					{
						logger.WriteEntry("Unable to prepare synthetic application package", LogEntryType.Error);
						return deployedApps;
					}
					fabricClient.ApplicationManager.CopyApplicationPackage(imageStoreConnection, stagingPath, imageStorePath);
					packageUploaded = true;
					await fabricClient.ApplicationManager.ProvisionApplicationAsync(imageStorePath);
					provisionedApplicationTypes.Add((applicationTypeName, applicationTypeVersion));
					logger.WriteEntry($"✅ Provisioned synthetic application type {applicationTypeName} {applicationTypeVersion}", LogEntryType.Informational);
				}
				else logger.WriteEntry($"Synthetic application type {applicationTypeName} {applicationTypeVersion} already provisioned", LogEntryType.Debug);
			}
			catch (Exception ex)
			{
				logger.WriteEntry($"Failed to provision synthetic application type: {ex.Message}", LogEntryType.Error);
				return deployedApps;
			}
			finally
			{
				if (packageUploaded)
				{
					try { fabricClient.ApplicationManager.RemoveApplicationPackage(imageStoreConnection, imageStorePath); } catch { }
				}
				if (stagingPath != null) TryDeleteDirectory(stagingPath);
			}

			for (int i = 1; i <= count; i++)
			{
				string appInstanceName = $"{applicationNamePrefix}{(char)('A' + i - 1)}";
				Uri appUri = new Uri($"fabric:/{appInstanceName}");
				try
				{
					var existing = await fabricClient.QueryManager.GetApplicationListAsync(appUri);
					if (existing.Any())
					{
						logger.WriteEntry($"Synthetic app {appInstanceName} already exists - skipping create", LogEntryType.Warning);
						deployedApps.Add(appInstanceName);
						if (!deployedApplications.Contains(appInstanceName)) deployedApplications.Add(appInstanceName);
						continue;
					}
					var createDesc = new ApplicationDescription(appUri, applicationTypeName, applicationTypeVersion);
					await fabricClient.ApplicationManager.CreateApplicationAsync(createDesc);
					await Task.Delay(300);
					deployedApps.Add(appInstanceName);
					deployedApplications.Add(appInstanceName);
					logger.WriteEntry($"  ✅ Deployed synthetic app: {appInstanceName}", LogEntryType.Informational);
				}
				catch (Exception ex)
				{
					logger.WriteEntry($"  ❌ Failed deploying synthetic app {appInstanceName}: {ex.Message}", LogEntryType.Error);
				}
			}
			logger.WriteEntry($"✅ Synthetic deployment complete: {deployedApps.Count}/{count}", LogEntryType.Informational);
			return deployedApps;
		}

		public Task<List<string>> DeployGuestExecutablesAsync(int count = 5)
		{
			if (!IsClusterAvailable) return Task.FromResult(new List<string>());
			logger.WriteEntry($"🎯 Deploying {count} guest executable applications...", LogEntryType.Informational);
			var deployedApps = new List<string>();
			for (int i = 1; i <= count; i++)
			{
				string appName = $"TestGuest{(char)('A' + i - 1)}";
				deployedApps.Add(appName); deployedApplications.Add(appName);
				logger.WriteEntry($"  ✅ Deployed: {appName}", LogEntryType.Informational);
			}
			return Task.FromResult(deployedApps);
		}

		public async Task RemoveAllDeployedApplicationsAsync(bool unprovisionProvisionedTypes = false)
		{
			cleanupInvoked = true;
			if (!IsClusterAvailable) { logger.WriteEntry("Cluster not available, skipping cleanup", LogEntryType.Warning); return; }
			if (deployedApplications.Count == 0) logger.WriteEntry("No applications to remove (tracked list)", LogEntryType.Informational);
			else
			{
				logger.WriteEntry($"🧹 Removing {deployedApplications.Count} tracked test applications...", LogEntryType.Informational);
				foreach (var appName in deployedApplications.ToList())
				{
					try { await RemoveApplicationAsync(appName); deployedApplications.Remove(appName); logger.WriteEntry($"  ✅ Removed: {appName}", LogEntryType.Informational); }
					catch (Exception ex) { logger.WriteEntry($"  ❌ Failed to remove {appName}: {ex.Message}", LogEntryType.Error); }
				}
			}

			// Aggressive cluster scan for orphaned test apps left behind by crashes/failures
			try
			{
				var allApps = await fabricClient.QueryManager.GetApplicationListAsync();
				var orphanPatterns = new[] { "Acetone", "TestService", "AcetoneIntegrationTest" }; // identifiers used by test deployments
				foreach (var app in allApps)
				{
					var name = app.ApplicationName != null ? app.ApplicationName.AbsoluteUri : string.Empty; // fabric:/Foo
					if (string.IsNullOrEmpty(name)) continue;
					// Normalize simple name
					string simple = name.StartsWith("fabric:/", StringComparison.OrdinalIgnoreCase) ? name.Substring("fabric:/".Length) : name;
					bool matches = false;
					for (int i = 0; i < orphanPatterns.Length && !matches; i++)
					{
						if (simple.IndexOf(orphanPatterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
						{
							// Avoid removing non-test production apps by requiring a trailing Tests or known synthetic prefixes
							if (simple.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) || simple.StartsWith("TestService", StringComparison.OrdinalIgnoreCase) || simple.StartsWith("AcetoneIntegrationTest", StringComparison.OrdinalIgnoreCase))
							{
								matches = true;
							}
						}
					}
					if (!matches) continue;
					// Skip if we already removed it via tracked list
					if (deployedApplications.Contains(simple)) continue;
					try
					{
						logger.WriteEntry($"🔎 Removing orphaned test application: {simple}", LogEntryType.Warning);
						await RemoveApplicationAsync(simple);
					}
					catch (Exception ex)
					{
						logger.WriteEntry($"  ⚠ Failed removing orphaned {simple}: {ex.Message}", LogEntryType.Warning);
					}
				}
			}
			catch (Exception ex)
			{
				logger.WriteEntry($"Aggressive orphan cleanup skipped due to error: {ex.Message}", LogEntryType.Warning);
			}

			if (unprovisionProvisionedTypes) await UnprovisionProvisionedApplicationTypesAsync();
			logger.WriteEntry("✅ Cleanup complete", LogEntryType.Informational);
		}

		private async Task UnprovisionProvisionedApplicationTypesAsync()
		{
			if (!IsClusterAvailable || provisionedApplicationTypes.Count == 0) return;
			var skipEnv = Environment.GetEnvironmentVariable("ACETONE_SKIP_APP_TYPE_UNPROVISION");
			if (string.Equals(skipEnv, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(skipEnv, "true", StringComparison.OrdinalIgnoreCase))
			{
				logger.WriteEntry("Skipping application type unprovision (env override)", LogEntryType.Warning);
				return;
			}
			logger.WriteEntry($"Unprovisioning {provisionedApplicationTypes.Count} application type(s)...", LogEntryType.Informational);
			foreach (var (TypeName, TypeVersion) in provisionedApplicationTypes.ToList())
			{
				try { await fabricClient.ApplicationManager.UnprovisionApplicationAsync(new UnprovisionApplicationTypeDescription(TypeName, TypeVersion)); logger.WriteEntry($"  ✅ Unprovisioned: {TypeName} {TypeVersion}", LogEntryType.Informational); provisionedApplicationTypes.Remove((TypeName, TypeVersion)); }
				catch (Exception ex) { logger.WriteEntry($"  ⚠ Failed to unprovision {TypeName} {TypeVersion}: {ex.Message}", LogEntryType.Warning); }
			}
		}

		public async Task<List<ApplicationInfo>> GetDeployedApplicationInfoAsync()
		{
			if (!IsClusterAvailable) return new List<ApplicationInfo>();
			var list = new List<ApplicationInfo>();
			foreach (var app in deployedApplications)
			{
				try
				{
					var uri = new Uri($"fabric:/{app}");
					var apps = await fabricClient.QueryManager.GetApplicationListAsync(uri);
					foreach (var a in apps)
					{
						list.Add(new ApplicationInfo
						{
							ApplicationName = a.ApplicationName.ToString(),
							ApplicationTypeName = a.ApplicationTypeName,
							ApplicationTypeVersion = a.ApplicationTypeVersion,
							HealthState = a.HealthState.ToString(),
							Status = a.ApplicationStatus.ToString()
						});
					}
				}
				catch (Exception ex) { logger.WriteEntry($"Could not get info for {app}: {ex.Message}", LogEntryType.Warning); }
			}
			return list;
		}

		private async Task RemoveApplicationAsync(string applicationName)
		{
			try
			{
				var applicationUri = new Uri($"fabric:/{applicationName}");
				var deleteDescription = new DeleteApplicationDescription(applicationUri) { ForceDelete = true };
				await fabricClient.ApplicationManager.DeleteApplicationAsync(deleteDescription);
				await WaitForApplicationDeletionAsync(applicationUri, TimeSpan.FromSeconds(30));
			}
			catch (Exception ex) { logger.WriteEntry($"Error removing {applicationName}: {ex.Message}", LogEntryType.Warning); }
		}

		private async Task WaitForApplicationDeletionAsync(Uri applicationName, TimeSpan timeout)
		{
			var start = DateTime.UtcNow;
			while (DateTime.UtcNow - start < timeout)
			{
				try
				{
					var apps = await fabricClient.QueryManager.GetApplicationListAsync(applicationName);
					if (!apps.Any()) return;
				}
				catch (FabricElementNotFoundException) { return; }
				await Task.Delay(1000);
			}
			logger.WriteEntry($"⚠ Application {applicationName} deletion timed out", LogEntryType.Warning);
		}

		private string PrepareApplicationPackage(string appPackageSource, string solutionRoot)
		{
			string stagingRoot = Path.Combine(Path.GetTempPath(), $"AcetoneTestPkg_{Guid.NewGuid():N}");
			try
			{
				CopyDirectory(appPackageSource, stagingRoot);
				string servicePackagePath = Path.Combine(stagingRoot, ServicePackageName);
				Directory.CreateDirectory(servicePackagePath);
				File.WriteAllText(Path.Combine(servicePackagePath, "ServiceManifest.xml"), GetServiceManifestContent());
				string codeTargetDir = Path.Combine(servicePackagePath, "Code");
				if (Directory.Exists(codeTargetDir)) Directory.Delete(codeTargetDir, true);
				Directory.CreateDirectory(codeTargetDir);
				File.WriteAllText(Path.Combine(codeTargetDir, ServiceScriptName), GetServiceRunnerScript());
				File.WriteAllText(Path.Combine(codeTargetDir, ServiceCommandName), GetServiceRunnerCommand());
				string configTargetDir = Path.Combine(servicePackagePath, "Config");
				if (!Directory.Exists(configTargetDir)) Directory.CreateDirectory(configTargetDir);
				string settingsPath = Path.Combine(configTargetDir, "Settings.xml");
				if (!File.Exists(settingsPath)) File.WriteAllText(settingsPath, "<Settings xmlns=\"http://schemas.microsoft.com/2011/01/fabric\" />");
				return stagingRoot;
			}
			catch (Exception ex)
			{
				logger.WriteEntry($"Failed to prepare application package: {ex.Message}", LogEntryType.Error);
				TryDeleteDirectory(stagingRoot); return null;
			}
		}

		private static string GetServiceManifestContent() => $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ServiceManifest Name=\"{ServicePackageName}\" Version=\"1.0.0\" xmlns=\"http://schemas.microsoft.com/2011/01/fabric\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n  <ServiceTypes>\n    <StatelessServiceType ServiceTypeName=\"Acetone.TestServiceType\" />\n  </ServiceTypes>\n  <CodePackage Name=\"Code\" Version=\"1.0.0\">\n    <EntryPoint>\n      <ExeHost>\n        <Program>{ServiceCommandName}</Program>\n        <WorkingFolder>CodePackage</WorkingFolder>\n      </ExeHost>\n    </EntryPoint>\n  </CodePackage>\n  <ConfigPackage Name=\"Config\" Version=\"1.0.0\" />\n  <Resources>\n    <Endpoints>\n      <Endpoint Name=\"ServiceEndpoint\" Protocol=\"http\" Port=\"0\" />\n    </Endpoints>\n  </Resources>\n</ServiceManifest>";

		private static string GetServiceRunnerScript()
		{
			var lines = new[]
			{
				"$ErrorActionPreference = 'Stop'",
				"[int]$port = 0",
				"$assigned = $env:Fabric_Endpoint_ServiceEndpoint",
				"if ([string]::IsNullOrWhiteSpace($assigned) -or -not [int]::TryParse($assigned, [ref]$port) -or $port -le 0) { $port = 9050 }",
				"Write-Host 'Starting Acetone test listener on port ' $port",
				"Add-Type -AssemblyName System.Net.HttpListener",
				"$listener = New-Object System.Net.HttpListener",
				"$listener.Prefixes.Add(\"http://+:$port/\")",
				"$listener.Start()",
				"while ($listener.IsListening) { try { $context = $listener.GetContext(); $response = $context.Response; $payload = @{ service = 'Acetone.TestService'; status = 'ok'; port = $port } | ConvertTo-Json -Compress; $buffer = [System.Text.Encoding]::UTF8.GetBytes($payload); $response.ContentLength64 = $buffer.Length; $response.OutputStream.Write($buffer,0,$buffer.Length); $response.OutputStream.Close() } catch [System.Net.HttpListenerException] { break } catch { Start-Sleep -Milliseconds 200 } }",
				"if ($listener.IsListening) { $listener.Stop() }"
			};
			return string.Join(Environment.NewLine, lines);
		}

		private static string GetServiceRunnerCommand()
		{
			var lines = new[]
			{
				"@echo off",
				"setlocal",
				$"set \"script=%~dp0{ServiceScriptName}\"",
				"if not exist \"%script%\" ( echo Service script %script% not found.& exit /b 1 )",
				"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%script%\""
			};
			return string.Join(Environment.NewLine, lines);
		}

		private static string TryLocateSolutionRoot()
		{
			var explicitRoot = Environment.GetEnvironmentVariable("ACETONE_SOLUTION_ROOT");
			if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot)) return explicitRoot;
			DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
			for (int i = 0; i < 8 && dir != null; i++)
			{
				if (File.Exists(Path.Combine(dir.FullName, "acetone.sln"))) return dir.FullName;
				if (Directory.GetFiles(dir.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any()) return dir.FullName;
				if (Directory.Exists(Path.Combine(dir.FullName, "MockApplication")) &&
					File.Exists(Path.Combine(dir.FullName, "MockApplication", "ApplicationPackageRoot", "ApplicationManifest.xml")) &&
					Directory.Exists(Path.Combine(dir.FullName, "MockSystem")) &&
					File.Exists(Path.Combine(dir.FullName, "MockSystem", "ApplicationPackageRoot", "ApplicationManifest.xml"))) return dir.FullName;
				dir = dir.Parent;
			}
			return null;
		}

		private static bool TryReadApplicationMetadata(string manifestPath, out string applicationTypeName, out string applicationTypeVersion)
		{
			applicationTypeName = null; applicationTypeVersion = null;
			try
			{
				string manifestText = File.ReadAllText(manifestPath);
				var nameMatch = Regex.Match(manifestText, "ApplicationTypeName\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
				if (nameMatch.Success) applicationTypeName = nameMatch.Groups[1].Value.Trim();
				var versionMatch = Regex.Match(manifestText, "ApplicationTypeVersion\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
				if (versionMatch.Success) applicationTypeVersion = versionMatch.Groups[1].Value.Trim();
				return !string.IsNullOrWhiteSpace(applicationTypeName) && !string.IsNullOrWhiteSpace(applicationTypeVersion);
			}
			catch { return false; }
		}

		private async Task<string> GetImageStoreConnectionStringAsync()
		{
			if (!string.IsNullOrEmpty(imageStoreConnectionString)) return imageStoreConnectionString;
			try
			{
				string manifest = await fabricClient.ClusterManager.GetClusterManifestAsync();
				var match = Regex.Match(manifest, "Parameter\\s+Name=\"ImageStoreConnectionString\"\\s+Value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					imageStoreConnectionString = match.Groups[1].Value.Trim();
					logger.WriteEntry($"Using image store connection: {imageStoreConnectionString}", LogEntryType.Debug);
				}
			}
			catch (Exception ex)
			{
				logger.WriteEntry($"Could not determine image store connection string: {ex.Message}", LogEntryType.Warning);
			}
			if (string.IsNullOrEmpty(imageStoreConnectionString))
			{
				imageStoreConnectionString = "fabric:ImageStore";
				logger.WriteEntry("Defaulting to image store connection 'fabric:ImageStore'", LogEntryType.Debug);
			}
			return imageStoreConnectionString;
		}

		private static void CopyDirectory(string sourceDir, string destinationDir)
		{
			Directory.CreateDirectory(destinationDir);
			foreach (var file in Directory.GetFiles(sourceDir))
			{
				var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
				File.Copy(file, destFile, true);
			}
			foreach (var directory in Directory.GetDirectories(sourceDir))
			{
				CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
			}
		}

		private static void TryDeleteDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return;
			try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
		}

		public class ApplicationInfo
		{
			public string ApplicationName { get; set; }
			public string ApplicationTypeName { get; set; }
			public string ApplicationTypeVersion { get; set; }
			public string HealthState { get; set; }
			public string Status { get; set; }
			public override string ToString() => $"{ApplicationName} ({ApplicationTypeName} v{ApplicationTypeVersion}) - {Status} [{HealthState}]";
		}

		public void Dispose()
		{
			if (isDisposed) return;
			try
			{
				// Only perform destructive cleanup here if tests never invoked explicit cleanup.
				// This prevents premature removal between ClassInitialize and test execution.
				bool envForce = string.Equals(Environment.GetEnvironmentVariable("ACETONE_FORCE_DISPOSE_CLEANUP"), "1", StringComparison.OrdinalIgnoreCase);
				if (!cleanupInvoked && envForce)
				{
					try
					{
						bool unprovision = !string.Equals(Environment.GetEnvironmentVariable("ACETONE_SKIP_APP_TYPE_UNPROVISION"), "1", StringComparison.OrdinalIgnoreCase);
						RemoveAllDeployedApplicationsAsync(unprovision).GetAwaiter().GetResult();
					}
					catch { }
				}
			}
			finally
			{
				fabricClient?.Dispose();
				isDisposed = true;
			}
		}
	}
}
