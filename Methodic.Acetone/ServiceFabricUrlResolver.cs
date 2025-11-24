using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Methodic.Acetone
{
	public class ServiceFabricUrlResolver : IServiceUrlResolver, IDisposable
	{
		private bool isDisposed;

		private static readonly object applicationLock = new object();
		private static readonly object serviceLock = new object();
		private const int PartitionResolveMaxAttempts = 10;
		private static readonly TimeSpan PartitionResolveInitialDelay = TimeSpan.FromMilliseconds(100); // reduced from 2s to 100ms
		private static readonly TimeSpan PartitionResolveAttemptTimeout = TimeSpan.FromSeconds(5); // tighter timeout per attempt
		private static readonly TimeSpan PartitionCacheTtl = TimeSpan.FromSeconds(30);
		private static readonly ConcurrentDictionary<string, (ResolvedServicePartition partition, DateTime timestamp)> PartitionCache = new ConcurrentDictionary<string, (ResolvedServicePartition, DateTime)>();

		public FabricClient Client
		{ get; private set; }

		protected List<string> ClusterConnectionStrings { get; }
		public ILogger Logger { get; }

		protected static Lazy<ConcurrentDictionary<string, Application>> CachedApplications = new Lazy<ConcurrentDictionary<string, Application>>(() =>
		{
			return new ConcurrentDictionary<string, Application>();
		});

		protected static Lazy<ConcurrentDictionary<string, Service>> CachedServices = new Lazy<ConcurrentDictionary<string, Service>>(() =>
		{
			return new ConcurrentDictionary<string, Service>();
		});

		public static int CachedApplicationCount
		{
			get
			{
				if (!CachedApplications.IsValueCreated)
				{
					return 0;
				}
				else
				{
					return CachedApplications.Value.Count;
				}
			}
		}
		public static int CachedServicesCount
		{
			get
			{
				return !CachedServices.IsValueCreated ? 0 : CachedServices.Value.Count;
			}
		}

		public static FabricClientSettings Settings
		{ get; } = new FabricClientSettings
		{
			ClientFriendlyName = "Methodic Acetone",
			ConnectionInitializationTimeout = TimeSpan.FromSeconds(2),
			KeepAliveInterval = TimeSpan.FromSeconds(10),
			NotificationCacheUpdateTimeout = TimeSpan.FromSeconds(2),
			NotificationGatewayConnectionTimeout = TimeSpan.FromSeconds(2),
			PartitionLocationCacheLimit = 5,
			PartitionLocationCacheBucketCount = 1024,
			ServiceChangePollInterval = TimeSpan.FromSeconds(5)
		};

		//Convenience methods
		public ServiceFabricUrlResolver(ILogger logger, string clusterConnectionString) : this(logger, new List<string> { clusterConnectionString })
		{ }

		public ServiceFabricUrlResolver(ILogger logger, List<string> clusterConnectionString)
			: this(logger, clusterConnectionString, skipFabricClientInitialization: false)
		{ }

		protected ServiceFabricUrlResolver(ILogger logger, List<string> clusterConnectionStrings, bool skipFabricClientInitialization, bool warmupCache = true)
		{
			this.Logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.ClusterConnectionStrings = clusterConnectionStrings ?? new List<string>();

			if (!skipFabricClientInitialization)
			{
				string[] endpoints = this.ClusterConnectionStrings.ToArray();
				InitializeClient(new FabricClient(Settings, endpoints), warmupCache);
			}
		}

		public ServiceFabricUrlResolver(ILogger logger, string clusterConnectionString, string clientCertificateSubjectDistinguishedName, string clientCertificateIssuerDistinguishedName, IList<String> remoteCertificateCommonNames)
		: this(logger, new List<string> { clusterConnectionString }, clientCertificateSubjectDistinguishedName, clientCertificateIssuerDistinguishedName, remoteCertificateCommonNames)
		{ }

		public ServiceFabricUrlResolver(ILogger logger, List<string> clusterConnectionStrings, string clientCertificateSubjectDistinguishedName, string clientCertificateIssuerDistinguishedName, IList<String> remoteCertificateCommonNames)
		{
			if (clusterConnectionStrings == default || !clusterConnectionStrings.Any())
			{
				throw new ArgumentException($"'{nameof(clusterConnectionStrings)}' cannot be null or empty.", nameof(clusterConnectionStrings));
			}

			if (string.IsNullOrEmpty(clientCertificateSubjectDistinguishedName))
			{
				throw new ArgumentException($"'{nameof(clientCertificateSubjectDistinguishedName)}' cannot be null or empty.", nameof(clientCertificateSubjectDistinguishedName));
			}

			if (string.IsNullOrEmpty(clientCertificateIssuerDistinguishedName))
			{
				throw new ArgumentException($"'{nameof(clientCertificateIssuerDistinguishedName)}' cannot be null or empty.", nameof(clientCertificateIssuerDistinguishedName));
			}

			this.Logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.ClusterConnectionStrings = clusterConnectionStrings;

			//Determine thumbprint based on provided distinguished subject name and issuer
			string resolvedThumbprint = null;

			//Open local certificate store for reading
			using (var certificateStore = new X509Store(StoreName.My.ToString(), StoreLocation.LocalMachine))
			{
				certificateStore.Open(OpenFlags.ReadOnly & OpenFlags.OpenExistingOnly);
				//Find all certificates by the client issuer
				var issuerResults = certificateStore.Certificates.Find(X509FindType.FindByIssuerDistinguishedName, clientCertificateIssuerDistinguishedName, true);

				//Filter the issuer results to only the subject certificate we are interested in
				var finalCert = issuerResults?.Find(X509FindType.FindBySubjectDistinguishedName, clientCertificateSubjectDistinguishedName, true);

				//If there is one result, that is the correct certificate, from the issuer that was configured, with the subject name that was provided
				if (finalCert?.Count == 1)
				{
					resolvedThumbprint = finalCert[0].Thumbprint;
				}
				else
				{
					if (finalCert == default || finalCert.Count == 0)
					{
						//Certificate not found!
						string error = $"Provided certificate distinguished subject name of {clientCertificateSubjectDistinguishedName} combined with an issuer distinguished name {clientCertificateIssuerDistinguishedName} yielded no results. Please make sure that the subject name resembles 'CN=*.methodic.com' and issuer distinguished name resembles 'CN=Sectigo RSA Domain Validation Secure Server CA, O=Sectigo Limited, L=Salford, S=Greater Manchester, C=GB'";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						certificateStore.Close();
						throw new ArgumentException(error, "Client Certificate Details");
					}
					if (finalCert.Count > 1)
					{
						var finalCertList = new List<X509Certificate2>();
						foreach (var c in finalCert)
						{
							finalCertList.Add(c);
						}
						var latest = finalCertList.OrderBy(c => c.NotBefore).First();
						resolvedThumbprint = latest.Thumbprint;
						this.Logger.WriteEntry($"Found {finalCertList.Count} certificates, and decided to use the latest with thumbprint of {resolvedThumbprint}", LogEntryType.Warning);
					}
				}
				certificateStore.Close();
			}
			var creds = new X509Credentials
			{
				FindType = X509FindType.FindByThumbprint,
				FindValue = resolvedThumbprint,
				StoreLocation = StoreLocation.LocalMachine,
				StoreName = StoreName.My.ToString()

			};
			if (remoteCertificateCommonNames != default)
			{
				foreach (string remoteCN in remoteCertificateCommonNames)
				{
					creds.RemoteCommonNames.Add(remoteCN);
				}
			}
			string[] endpoints = this.ClusterConnectionStrings.ToArray();
			InitializeClient(new FabricClient(creds, Settings, endpoints));


		}

		private void ServiceManager_ServiceNotificationFilterMatched(object sender, EventArgs e)
		{
			if (ServiceFabricUrlResolver.CachedServices.IsValueCreated)
			{
				Console.WriteLine("CLEARNING CACHE");
				this.Logger.WriteEntry("Received service notification event, clearing service cache", LogEntryType.Informational);
				ServiceFabricUrlResolver.CachedServices.Value.Clear();
			}
			// Invalidate partition cache as service topology may have changed
			PartitionCache.Clear();
		}

		public ServiceFabricUrlResolver(ILogger logger, string clusterConnectionString, string clientCertificateThumbprint, string serverCertificateThumbprint)
			: this(logger, new List<string> { clusterConnectionString }, clientCertificateThumbprint, new List<string> { serverCertificateThumbprint }) { }

		public ServiceFabricUrlResolver(ILogger logger, string clusterConnectionString, string clientCertificateThumbprint, List<string> serverCertificateThumbprints)
			: this(logger, new List<string> { clusterConnectionString }, clientCertificateThumbprint, serverCertificateThumbprints) { }

		public ServiceFabricUrlResolver(ILogger logger, List<string> clusterConnectionStrings, string clientCertificateThumbprint, List<string> serverCertificateThumbprints)
		{
			if (clusterConnectionStrings == default || !clusterConnectionStrings.Any())
			{
				throw new ArgumentException($"'{nameof(clusterConnectionStrings)}' cannot be null or empty.", nameof(clusterConnectionStrings));
			}

			if (string.IsNullOrEmpty(clientCertificateThumbprint))
			{
				throw new ArgumentException($"'{nameof(clientCertificateThumbprint)}' cannot be null or empty.", nameof(clientCertificateThumbprint));
			}

			this.Logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.ClusterConnectionStrings = clusterConnectionStrings;

			X509Credentials creds = new X509Credentials
			{
				FindType = X509FindType.FindByThumbprint,
				FindValue = clientCertificateThumbprint,
				StoreLocation = StoreLocation.LocalMachine,
				StoreName = StoreName.My.ToString()
			};

			if (serverCertificateThumbprints != default)
			{
				foreach (var tp in serverCertificateThumbprints)
				{
					creds.RemoteCertThumbprints.Add(tp);
				}
			}
			string[] endpoints = this.ClusterConnectionStrings.ToArray();
			InitializeClient(new FabricClient(creds, Settings, endpoints));
		}

		private void InitializeClient(FabricClient client, bool warmupCache = true)
		{
			this.Client = client;
			this.Client.ServiceManager.ServiceNotificationFilterMatched += this.ServiceManager_ServiceNotificationFilterMatched;

			if (warmupCache)
			{
				this.WarmupCache();
			}
		}

		private void WarmupCache()
		{
			this.WarmupCacheCore();
		}

		protected virtual void WarmupCacheCore()
		{
			this.Logger.WriteEntry("BEGIN CACHE WARMUP", LogEntryType.Informational);
			if (this.Client == null)
			{
				this.Logger.WriteEntry("FabricClient not initialised – skipping cache warmup", LogEntryType.Debug);
				this.Logger.WriteEntry("END CACHE WARMUP", LogEntryType.Informational);
				return;
			}

			try
			{
				var list = this.Client.QueryManager.GetApplicationTypeListAsync().GetAwaiter().GetResult();
				foreach (var app in list)
				{
					try
					{
						this.ResolveServiceUri(app.ApplicationTypeName.Replace("Type", string.Empty), Guid.NewGuid(), null, true).Wait();
					}
					catch (Exception ex)
					{
						this.Logger.WriteEntry($"Encountered an exception warming up the cache for {app.ApplicationTypeName}: {ServiceFabricLocator.FormatException(ex)}", LogEntryType.Warning);
					}
				}
			}
			catch (FabricException ex)
			{
				this.Logger.WriteEntry($"FabricClient cache warmup skipped due to FabricException: {ex.Message}", LogEntryType.Warning);
			}
			catch (TimeoutException ex)
			{
				this.Logger.WriteEntry($"FabricClient cache warmup timed out: {ex.Message}", LogEntryType.Warning);
			}
			catch (Exception ex)
			{
				this.Logger.WriteEntry($"FabricClient cache warmup failed: {ServiceFabricLocator.FormatException(ex)}", LogEntryType.Warning);
			}
			finally
			{
				this.Logger.WriteEntry("END CACHE WARMUP", LogEntryType.Informational);
			}
		}

		public async Task<string> ResolveServiceUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			return await ResolveServiceUriCore(applicationName, invocationId, version, refreshCache);
		}

		protected virtual async Task<string> ResolveServiceUriCore(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			this.Logger.WriteEntry($"Starting to resolve url for application {applicationName}, invocation {invocationId}", LogEntryType.Informational);

			if (string.IsNullOrWhiteSpace(applicationName))
			{
				throw new ArgumentException("Application name is required", nameof(applicationName));
			}

			string cacheKey = applicationName.ToUpperInvariant() + (version ?? "-no-service-version");
			Application application;

			if (!refreshCache && CachedApplications.Value.TryGetValue(cacheKey, out application))
			{
				this.Logger.WriteEntry($"Cache hit for {cacheKey}, found application {application.ApplicationName}, invocation {invocationId}", LogEntryType.Debug);
				return await this.StatelessEndpointUri(application, invocationId);
			}

			lock (applicationLock)
			{
				if (refreshCache || !CachedApplications.Value.TryGetValue(cacheKey, out application))
				{
					this.Logger.WriteEntry($"Cache for key {cacheKey} was not found, so we'll construct it", LogEntryType.Debug);
					application = GetApplicationMetadata(applicationName, version, invocationId);
					_ = CachedApplications.Value.AddOrUpdate(cacheKey, application, (k, v) => application);
				}
			}

			return await StatelessEndpointUri(application, invocationId);
		}

		private Application GetApplicationMetadata(string applicationName, string version, Guid invocationId)
		{
			if (this.Client == null)
			{
				throw new InvalidOperationException("Service Fabric client has not been initialised. This resolver is operating in mock-only mode.");
			}

			var getApplicationsTask = this.Client.QueryManager.GetApplicationListAsync();
			Task.WaitAll(getApplicationsTask);
			var allApplications = getApplicationsTask.Result?.ToList();

			if (allApplications == null || !allApplications.Any())
			{
				string noAppError = $"No matching applications with applicationTypeName of {applicationName} could be found on cluster {string.Join(", ", this.ClusterConnectionStrings)}. Has the application been deployed with the same name?";
				this.Logger.WriteEntry(noAppError, LogEntryType.Error);
				throw new KeyNotFoundException(noAppError);
			}

			var matchingApplications = FilterApplicationsByTypeName(allApplications, applicationName);
			if (!matchingApplications.Any())
			{
				matchingApplications = FilterApplicationsByApplicationName(allApplications, applicationName);
			}
			else
			{
				var narrowedByExactName = FilterApplicationsByApplicationName(matchingApplications, applicationName);
				if (narrowedByExactName.Count == 1)
				{
					matchingApplications = narrowedByExactName;
				}
			}

			if (!matchingApplications.Any())
			{
				string error = $"Could not find any application matching {applicationName}, invocation {invocationId}";
				this.Logger.WriteEntry(error, LogEntryType.Error);
				throw new KeyNotFoundException(error);
			}

			if (matchingApplications.Count > 1)
			{
				if (!string.IsNullOrEmpty(version))
				{
					matchingApplications = matchingApplications.Where(a => a.ApplicationTypeVersion.Equals(version, StringComparison.InvariantCultureIgnoreCase)).ToList();
				}

				if (!matchingApplications.Any())
				{
					string error = $"Could not find any application matching {applicationName} with version of {version}, invocation {invocationId}";
					this.Logger.WriteEntry(error, LogEntryType.Error);
					throw new KeyNotFoundException(error);
				}

				var normalizedTarget = NormalizeApplicationIdentifier(applicationName);
				var exactNameMatches = matchingApplications.Where(a => NormalizeApplicationIdentifier(a.ApplicationName?.AbsoluteUri).Equals(normalizedTarget, StringComparison.InvariantCultureIgnoreCase)).ToList();
				if (exactNameMatches.Count == 1)
				{
					matchingApplications = exactNameMatches;
				}
				else if (exactNameMatches.Count > 1)
				{
					matchingApplications = exactNameMatches;
				}

				if (matchingApplications.Count > 1)
				{
					var readyApplications = matchingApplications.Where(a => a.ApplicationStatus == ApplicationStatus.Ready).ToList();
					if (readyApplications.Any())
					{
						matchingApplications = readyApplications;
					}
				}

				if (matchingApplications.Count > 1)
				{
					var selected = matchingApplications.OrderBy(a => NormalizeApplicationIdentifier(a.ApplicationName?.AbsoluteUri)).First();
					this.Logger.WriteEntry($"Multiple applications matched {applicationName}. Selecting {selected.ApplicationName}", LogEntryType.Warning);
					return selected;
				}
			}

			return matchingApplications.Single();
		}

		private static List<Application> FilterApplicationsByTypeName(IEnumerable<Application> applications, string applicationName)
		{
			if (applications == null)
			{
				return new List<Application>();
			}

			string normalizedTarget = NormalizeApplicationIdentifier(applicationName);
			return applications.Where(a =>
			{
				string normalisedTypeName = NormalizeApplicationType(a.ApplicationTypeName);
				return normalisedTypeName.Equals(normalizedTarget, StringComparison.InvariantCultureIgnoreCase);
			}).ToList();
		}

		private static List<Application> FilterApplicationsByApplicationName(IEnumerable<Application> applications, string applicationName)
		{
			if (applications == null)
			{
				return new List<Application>();
			}

			string normalizedTarget = NormalizeApplicationIdentifier(applicationName);
			return applications.Where(a =>
			{
				var candidate = NormalizeApplicationIdentifier(a.ApplicationName?.AbsoluteUri);
				return candidate.Equals(normalizedTarget, StringComparison.InvariantCultureIgnoreCase);
			}).ToList();
		}

		private static string NormalizeApplicationType(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return string.Empty;
			}

			value = value.Trim();
			int index = value.LastIndexOf("type", StringComparison.InvariantCultureIgnoreCase);
			if (index >= 0)
			{
				value = value.Substring(0, index);
			}

			return NormalizeApplicationIdentifier(value);
		}

		private static string NormalizeApplicationIdentifier(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return string.Empty;
			}

			value = value.Trim();
			if (value.StartsWith("fabric:/", StringComparison.InvariantCultureIgnoreCase))
			{
				value = value.Substring("fabric:/".Length);
			}
			else if (value.StartsWith("fabric:", StringComparison.InvariantCultureIgnoreCase))
			{
				value = value.Substring("fabric:".Length);
			}

			value = value.Trim('/');

			// NEW: Treat underscores and hyphens as equivalent by normalising all underscores to hyphens.
			// This allows clusters that deploy applications as Guard_PR12906 to match incoming PR pattern Guard-PR12906.
			if (value.IndexOf('_') >= 0)
			{
				value = value.Replace('_', '-');
			}

			return value;
		}

		public async Task<string> ResolveFunctionUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			return await ResolveFunctionUriCore(applicationName, invocationId, version, refreshCache);
		}

		protected virtual async Task<string> ResolveFunctionUriCore(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			this.Logger.WriteEntry($"Starting to resolve url for function in {applicationName}, invocation {invocationId}", LogEntryType.Informational);

			string cacheKey = $"{applicationName}-FKT-{version ?? "no-version"}";
			Application application;

			if (!refreshCache && CachedApplications.Value.TryGetValue(cacheKey, out application))
			{
				this.Logger.WriteEntry($"Cache hit for {cacheKey}, found application {application.ApplicationName}, invocation {invocationId}", LogEntryType.Informational);
				return await this.FunctionEndpointUri(application, invocationId);
			}

			lock (applicationLock)
			{
				if (refreshCache || !CachedApplications.Value.TryGetValue(cacheKey, out application))
				{
					System.Diagnostics.Trace.WriteLine($"Cache for key {cacheKey} was not found, so we'll construct it");
					application = GetApplicationMetadata(applicationName, version, invocationId);
					_ = CachedApplications.Value.AddOrUpdate(cacheKey, application, (k, v) => application);
				}
			}
			return await FunctionEndpointUri(application, invocationId);
		}

		public async Task<string> StatelessEndpointUri(Application applicationInfo, Guid invocationId, bool refreshCache = false)
		{
			if (applicationInfo == null)
			{
				throw new ArgumentNullException(nameof(applicationInfo));
			}
			string cacheKey = applicationInfo.ApplicationName.AbsoluteUri;

			if (!refreshCache && CachedServices.Value.TryGetValue(cacheKey, out var serviceInfo))
			{
				return await this.PartitionEndpoint(serviceInfo, invocationId);
			}

			lock (serviceLock)
			{
				if (refreshCache || !CachedServices.Value.TryGetValue(cacheKey, out serviceInfo))
				{
					var getServiceInfoTask = this.Client.QueryManager.GetServiceListAsync(applicationInfo.ApplicationName);
					Task.WaitAll(getServiceInfoTask);
					var serviceInfoList = getServiceInfoTask.Result.ToList();
					if (serviceInfoList == null)
					{
						throw new KeyNotFoundException($"Not a single service was found within application {applicationInfo.ApplicationName}");
					}

					var statelessServices = serviceInfoList.Where(s => s.ServiceKind == System.Fabric.Query.ServiceKind.Stateless && (s.ServiceTypeName.ToUpperInvariant().Contains("API") || s.ServiceTypeName.ToUpperInvariant().Contains("SERVICE"))).ToList();

					if (statelessServices == null || !statelessServices.Any())
					{
						string error = $"Application {applicationInfo.ApplicationName} has no matching stateless services (expects a single *API* or *Service* in type name), invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}
					if (statelessServices.Count > 1)
					{
						string error = $"{statelessServices.Count} stateless services matched heuristic (API/SERVICE) for application {applicationInfo.ApplicationName}. Invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new Exception(error);
					}
					serviceInfo = statelessServices.Single();

					_ = CachedServices.Value.AddOrUpdate(cacheKey, serviceInfo, (k, v) =>
					{
						var svcdesc = new System.Fabric.Description.ServiceNotificationFilterDescription(serviceInfo.ServiceName, true, false);
						this.Client.ServiceManager.RegisterServiceNotificationFilterAsync(svcdesc).Wait();
						this.Logger.WriteEntry($"Adding to cache {cacheKey}, service {serviceInfo.ServiceTypeName}, invocation {invocationId}", LogEntryType.Informational);
						return serviceInfo;
					});
				}
			}
			return await PartitionEndpoint(CachedServices.Value[cacheKey], invocationId);
		}

		public async Task<string> FunctionEndpointUri(Application applicationInfo, Guid invocationId, bool refreshCache = false)
		{
			if (applicationInfo is null)
			{
				throw new ArgumentNullException(nameof(applicationInfo));
			}

			//Service cache key is pretty simple for function services, it's the application name as we only support a single function service for a given application
			string cacheKey = applicationInfo.ApplicationName.AbsoluteUri + "-FKT";

			//Try and get the service information from cache so as to not query the cluster
			if (!refreshCache && CachedServices.Value.TryGetValue(cacheKey, out var serviceInfo))
			{
				return await this.PartitionEndpoint(serviceInfo, invocationId);
			}

			//Serialize threads while adding to cache
			lock (serviceLock)
			{
				//Double check lock
				if (refreshCache || !CachedServices.Value.TryGetValue(cacheKey, out serviceInfo))
				{
					//Get all services for the unique application
					var getServiceInfoTask = this.Client.QueryManager.GetServiceListAsync(applicationInfo.ApplicationName);
					Task.WaitAll(getServiceInfoTask);
					var serviceInfoList = getServiceInfoTask.Result.ToList();
					if (serviceInfoList == null)
					{
						throw new KeyNotFoundException($"Not a single service was found within the determined application with name {applicationInfo.ApplicationName}, type name of {applicationInfo.ApplicationTypeName} and type version of {applicationInfo.ApplicationTypeVersion}");
					}

					//Find the stateless service within the given application which is the API
					var statelessServices = serviceInfoList.Where(s => s.ServiceKind == System.Fabric.Query.ServiceKind.Stateless && s.ServiceTypeName.ToUpperInvariant().Contains("FUNCTION")).ToList();
					if (statelessServices == null || !statelessServices.Any())
					{
						string error = $"Application with name {applicationInfo.ApplicationName} has no function services which are stateless and have 'FUNCTION' in the type name, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}

					if (statelessServices.Count > 1)
					{
						string error = $"{statelessServices.Count} stateless services with 'FUNCTION' in the name was discovered for application name {applicationInfo.ApplicationName} which is too ambiguous, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new Exception(error);
					}
					serviceInfo = statelessServices.Single();


					//Add to cache for following threads
					_ = CachedServices.Value.AddOrUpdate(cacheKey, serviceInfo, (k, v) =>
					{
						var svcdesc = new System.Fabric.Description.ServiceNotificationFilterDescription(serviceInfo.ServiceName, true, false);
						this.Client.ServiceManager.RegisterServiceNotificationFilterAsync(svcdesc).Wait();
						this.Logger.WriteEntry($"Adding to cache {cacheKey}, application {serviceInfo.ServiceTypeName}, invocation {invocationId}", LogEntryType.Informational);
						return serviceInfo;
					});
				}
			}
			return await PartitionEndpoint(serviceInfo, invocationId);
		}

		public async Task<string> PartitionEndpoint(Service service, Guid invocationId)
		{
			if (service is null)
			{
				string error = $"Service is null, invocation {invocationId}";
				this.Logger.WriteEntry(error, LogEntryType.Error);
				throw new ArgumentNullException(nameof(service), error);
			}

			this.Logger.WriteEntry($"Getting url for service {service.ServiceName}, invocation {invocationId}", LogEntryType.Informational);

			ResolvedServicePartition cachedServicePartition = null;
			string cacheKey = service.ServiceName.AbsoluteUri;
			bool cacheDisabled = string.Equals(Environment.GetEnvironmentVariable("ACETONE_DISABLE_PARTITION_CACHE"), "1", StringComparison.OrdinalIgnoreCase);
			if (!cacheDisabled && PartitionCache.TryGetValue(cacheKey, out var entry))
			{
				if (DateTime.UtcNow - entry.timestamp < PartitionCacheTtl)
				{
					cachedServicePartition = entry.partition;
					this.Logger.WriteEntry($"Partition cache hit for {service.ServiceName}", LogEntryType.Debug);
				}
				else
				{
					PartitionCache.TryRemove(cacheKey, out _);
				}
			}

			if (cachedServicePartition == null)
			{
				cachedServicePartition = await ResolveServicePartitionWithRetryAsync(service.ServiceName, invocationId);
				if (!cacheDisabled)
				{
					PartitionCache[cacheKey] = (cachedServicePartition, DateTime.UtcNow);
				}
			}

			this.Logger.WriteEntry($"Found partition {cachedServicePartition.Info.Id}, invocation {invocationId}", LogEntryType.Debug);
			var cachedEndpoint = cachedServicePartition.GetEndpoint();
			var endpoint = cachedEndpoint;
			this.Logger.WriteEntry($"Endpoint selected for {service.ServiceName} partition is {cachedEndpoint.Address}, {cachedEndpoint.Role}, invocation {invocationId}", LogEntryType.Debug);

			//var servicePartition = await this.Client.ServiceManager.ResolveServicePartitionAsync(service.ServiceName, cachedServicePartition);

			//this.Logger.WriteEntry($"Found (current) partition {cachedServicePartition.Info.Id}, invocation {invocationId}", LogEntryType.Informational);

			//var endpoint = servicePartition.GetEndpoint();

			this.Logger.WriteEntry($"Selected endpoint for {service.ServiceName} is {endpoint.Address}, {endpoint.Role}, invocation {invocationId}", LogEntryType.Informational);

			//If there are no endpoints, then error
			if (endpoint == default)
			{
				string error = $"No endpoints are available for service name {service.ServiceName}, invocation {invocationId}";
				this.Logger.WriteEntry(error, LogEntryType.Error);
				throw new Exception(error);
			}

			string address = endpoint.Address;
			//For some reason, the address contains a JSON object, not sure why but this will deal with both scenarios
			if (address.Contains('{')) //JSON Result on endpoint address?
			{
				address = ServiceFabricUrlParser.EndpointJsonToText(endpoint.Address, this.Logger);
			}

			// Normalize local addresses
			address = ServiceFabricUrlParser.NormalizeLocalEndpoint(address, this.Logger);

			//If there are multiple, then just return the first(?)
			return address;
		}

		private async Task<ResolvedServicePartition> ResolveServicePartitionWithRetryAsync(Uri serviceName, Guid invocationId)
		{
			Exception lastException = null;
			TimeSpan delay = PartitionResolveInitialDelay;

			for (int attempt = 1; attempt <= PartitionResolveMaxAttempts; attempt++)
			{
				try
				{
					var partition = await this.Client.ServiceManager.ResolveServicePartitionAsync(serviceName, PartitionResolveAttemptTimeout);
					if (partition != null)
					{
						return partition;
					}
				}
				catch (FabricTransientException ex)
				{
					lastException = ex;
					this.Logger.WriteEntry($"Partition resolve transient failure for {serviceName}: {ex.Message}", LogEntryType.Debug);
				}
				catch (TimeoutException ex)
				{
					lastException = ex;
					this.Logger.WriteEntry($"Partition resolve timeout for {serviceName}: {ex.Message}", LogEntryType.Debug);
				}

				if (attempt < PartitionResolveMaxAttempts)
				{
					await Task.Delay(delay);
					// Exponential backoff with cap at 2 seconds
					delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
				}
			}

			throw lastException ?? new TimeoutException($"Unable to resolve partition for {serviceName} after {PartitionResolveMaxAttempts} attempts, invocation {invocationId}");
		}

		/// <summary>
		/// Extracts the endpoint URL from a Service Fabric endpoint JSON string.
		/// This method delegates to ServiceFabricUrlParser for the actual parsing logic.
		/// </summary>
		/// <param name="json">The JSON string or plain URL from the Service Fabric endpoint.</param>
		/// <param name="logger">Logger for diagnostic output.</param>
		/// <returns>The extracted endpoint URL.</returns>
		public static string EndpointJsonToText(string json, ILogger logger)
		{
			return ServiceFabricUrlParser.EndpointJsonToText(json, logger);
		}

		/// <summary>
		/// Extracts the Service Fabric application name from a URL based on the specified location strategy.
		/// This method delegates to ServiceFabricUrlParser for the actual parsing logic.
		/// </summary>
		/// <param name="url">The URL to parse. May or may not include the protocol scheme.</param>
		/// <param name="nameLocation">Specifies where in the URL the application name is located.</param>
		/// <param name="applicationName">Output parameter containing the extracted application name.</param>
		/// <returns>True if the application name was successfully extracted; otherwise false.</returns>
		public static bool TryGetApplicationNameFromUrl(string url, ApplicationNameLocation nameLocation, out string applicationName)
		{
			return ServiceFabricUrlParser.TryGetApplicationNameFromUrl(url, nameLocation, out applicationName);
		}

		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (isDisposed)
			{
				return;
			}

			if (disposing)
			{
				// free managed resources
				this.Client?.Dispose();
			}
			isDisposed = true;
		}
	}
}
