

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

		public FabricClient Client
		{ get; private set; }

		private List<string> ClusterConnectionStrings { get; }
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
				if (!CachedServices.IsValueCreated)
				{
					return 0;
				}
				else
				{
					return CachedServices.Value.Count;
				}
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
		{
			this.Logger = logger;
			this.ClusterConnectionStrings = clusterConnectionString;
			string[] endpoints = this.ClusterConnectionStrings.ToArray();
			this.Client = new FabricClient(Settings, endpoints);
			this.Client.ServiceManager.ServiceNotificationFilterMatched += this.ServiceManager_ServiceNotificationFilterMatched;
			this.WarmupCache();
		}

		public ServiceFabricUrlResolver(ILogger logger, string clusterConnectionString, string clientCertificateSubjectDistinguishedName, string clientCertificateIssuerDistinguishedName, IList<string> remoteCertificateCommonNames)
		: this(logger, new List<string> { clusterConnectionString }, clientCertificateSubjectDistinguishedName, clientCertificateIssuerDistinguishedName, remoteCertificateCommonNames)
		{ }

		public ServiceFabricUrlResolver(ILogger logger, List<string> clusterConnectionStrings, string clientCertificateSubjectDistinguishedName, string clientCertificateIssuerDistinguishedName, IList<string> remoteCertificateCommonNames)
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
			this.Client = new FabricClient(creds, Settings, endpoints);
			this.Client.ServiceManager.ServiceNotificationFilterMatched += this.ServiceManager_ServiceNotificationFilterMatched;
			this.WarmupCache();


		}

		private void ServiceManager_ServiceNotificationFilterMatched(object sender, EventArgs e)
		{
			if (ServiceFabricUrlResolver.CachedServices.IsValueCreated)
			{
				Console.WriteLine("CLEARNING CACHE");
				this.Logger.WriteEntry("Received service notification event, clearing service cache", LogEntryType.Informational);
				ServiceFabricUrlResolver.CachedServices.Value.Clear();
			}
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
			this.Client = new FabricClient(creds, Settings, endpoints);
			this.Client.ServiceManager.ServiceNotificationFilterMatched += this.ServiceManager_ServiceNotificationFilterMatched;
			this.WarmupCache();
		}

		private void WarmupCache()
		{
			//Warm up cache
			this.Logger.WriteEntry("BEGIN CACHE WARMUP", LogEntryType.Informational);
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
			this.Logger.WriteEntry("END CACHE WARMUP", LogEntryType.Informational);
		}

		public async Task<string> ResolveServiceUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			this.Logger.WriteEntry($"Starting to resolve url for application {applicationName}, invocation {invocationId}", LogEntryType.Informational);

			//Create a cache key to refer to the application and version combination
			string cacheKey = applicationName.ToUpperInvariant();
			cacheKey += version ?? "-no-service-version";

			//Attempt to retrieve the application details from cache and continue to service endpoint resolution
			if (!refreshCache && CachedApplications.Value.TryGetValue(cacheKey, out var application))
			{
				this.Logger.WriteEntry($"Cache hit for {cacheKey}, found application {application.ApplicationName}, invocation {invocationId}", LogEntryType.Debug);
				return await this.StatelessEndpointUri(application, invocationId);
			}
			//If the cache is a miss, then constrain threads to serial, and populate the cache
			lock (applicationLock)
			{
				//double check lock (in case a thread ahead of this one was able to set the cache prior)
				if (refreshCache || !CachedApplications.Value.TryGetValue(cacheKey, out application))
				{
					//Can't use async/await inside of locks. Wait for the Task to complete
					this.Logger.WriteEntry($"Cache for key {cacheKey} was not found, so we'll construct it", LogEntryType.Debug);       //Write out a diagnostic message for cache miss and rebuild
					var applications = this.Client.QueryManager.GetApplicationListAsync().GetAwaiter().GetResult()?.ToList();           //Call the SF cluster and query the list of available applications


					//If we didn't find any applications, then exit early by throwing an exception
					if (applications == default || !applications.Any())
					{
						this.Logger.WriteEntry($"No application found for {applicationName}, invocation {invocationId}", LogEntryType.Error);
						throw new KeyNotFoundException($"No matching applications with applicationTypeName of {applicationName} could be found on cluster {string.Join(", ", this.ClusterConnectionStrings)}. Has the application been deployed to the cluster with the same name?");
					}
					applications = applications.Where(a =>
					{
						string applicationTypeName = a.ApplicationTypeName;

						if (applicationTypeName.EndsWith("type", StringComparison.InvariantCultureIgnoreCase))
						{
							applicationTypeName = applicationTypeName.Substring(0, applicationTypeName.LastIndexOf("type", StringComparison.InvariantCultureIgnoreCase));
						}
						return applicationTypeName.Equals(applicationName, StringComparison.InvariantCultureIgnoreCase);
					}).ToList();

					//If we find more than a single version of the application running, then we must distinguish it by version.
					if (applications.Count > 1)
					{
						if (string.IsNullOrEmpty(version))
						{
							string formattedApps = string.Join(", ", applications.Select(a => a.ApplicationTypeName + "_" + a.ApplicationTypeVersion).ToArray());
							this.Logger.WriteEntry($"{applications.Count} application types for {applicationName} were found, please provide a version to determine a single type, invocation {invocationId}. {formattedApps}", LogEntryType.Error);
							throw new ArgumentNullException(nameof(version), $"{applications.Count} application types for {applicationName} were found, please provide a version to determine a single type. {formattedApps}");
						}
						applications = applications.Where(a => a.ApplicationTypeVersion.Equals(version, StringComparison.InvariantCultureIgnoreCase)).ToList();
						if (applications == default || !applications.Any())
						{
							this.Logger.WriteEntry($"{applications.Count} application types with name {applicationName} were found, none with version {version}. invocation {invocationId}", LogEntryType.Error);
						}
					}

					//If we still have more than a single application after filtering by version, then we have a scenario which we do not currently support in this rewrite module.
					if (applications.Count > 1)
					{
						string error = $"Ambiguous applications were discovered with an application name of {applicationName} with version of {version ?? "(NULL)"}, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new Exception(error);
					}
					application = applications.SingleOrDefault();

					//Some basic sanity checks
					if (applications == default || applications.Count == 0)
					{
						string error = $"Could not find any application type matching {applicationName}, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}
					if (!string.IsNullOrEmpty(version) && !application.ApplicationTypeVersion.Equals(version, StringComparison.InvariantCultureIgnoreCase))
					{
						string error = $"Could not find any application type matching {applicationName} with version of {version}, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}

					//Add to cache for the following threads
					_ = CachedApplications.Value.AddOrUpdate(cacheKey, application, (key, oldVal) =>
					{
						this.Logger.WriteEntry($"Adding to cache {key}, application {application.ApplicationName}, invocation {invocationId}", LogEntryType.Informational);
						return application;
					});
				}
			}
			return await StatelessEndpointUri(application, invocationId);
		}

		public async Task<string> ResolveFunctionUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false)
		{
			this.Logger.WriteEntry($"Starting to resolve url for function in {applicationName}, invocation {invocationId}", LogEntryType.Informational);

			//Create a cache key to refer to the application and version combination
			string cacheKey = $"{applicationName}-FKT-{version ?? "no-version"}";

			//Attempt to retrieve the application details from cache and continue to service endpoint resolution
			if (!refreshCache && CachedApplications.Value.TryGetValue(cacheKey, out var application))
			{
				this.Logger.WriteEntry($"Cache hit for {cacheKey}, found application {application.ApplicationName}, invocation {invocationId}", LogEntryType.Informational);
				return await this.FunctionEndpointUri(application, invocationId);
			}

			//If the cache is a miss, then constrain threads to serial, and populate the cache
			lock (applicationLock)
			{
				//double check lock (in case a thread ahead of this one was able to set the cache prior)
				if (refreshCache || !CachedApplications.Value.TryGetValue(cacheKey, out application))
				{
					System.Diagnostics.Trace.WriteLine($"Cache for key {cacheKey} was not found, so we'll construct it");   //Write out a diagnostic message for cache miss and rebuild
					var getApplicationsTask = this.Client.QueryManager.GetApplicationListAsync();                           //Call the SF cluster and query the list of available applications
					Task.WaitAll(getApplicationsTask);                                                                      //Can't use async/await inside of locks. Wait for the Task to complete
					var applications = getApplicationsTask.Result?.ToList();

					//If we didn't find any applications, then exit early by throwing an exception
					if (applications == default || !applications.Any())
					{
						string error = $"No matching applications with applicationTypeName of {applicationName} could be found on cluster {string.Join(", ", this.ClusterConnectionStrings)}. Has the application been deployed to the cluster with the same name? invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}
					applications = applications.Where(a =>
					{
						return a.ApplicationTypeName.StartsWith(applicationName, StringComparison.InvariantCultureIgnoreCase);
					}).ToList();

					//If we find more than a single version of the application running, then we must distinguish it by version.
					if (applications.Count > 1)
					{
						if (string.IsNullOrEmpty(version))
						{
							string error = $"{applications.Count} application types were found, please provide a version to determine a single type, invocation {invocationId}";
							this.Logger.WriteEntry(error, LogEntryType.Error);
							throw new ArgumentNullException(nameof(version), error);
						}
						applications = applications.Where(a => a.ApplicationTypeVersion.Equals(version, StringComparison.InvariantCultureIgnoreCase)).ToList();
						if (applications == default || !applications.Any())
						{
							this.Logger.WriteEntry($"{applications.Count} application types were found, none with version {version}, invocation {invocationId}", LogEntryType.Error);
						}
					}

					//If we still have more than a single application after filtering by version, then we have a scenario which we do not currently support in this rewrite module.
					if (applications.Count > 1)
					{
						string error = $"Ambiguous applications were discovered with an application name of {applicationName} with version of {version ?? "(NULL)"}, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new Exception(error);
					}
					application = applications.SingleOrDefault();

					//Some basic sanity checks
					if (applications == default || applications.Count == 0)
					{
						string error = $"Could not find any application type matching {applicationName}, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}
					if (!string.IsNullOrEmpty(version) && !application.ApplicationTypeVersion.Equals(version, StringComparison.InvariantCultureIgnoreCase))
					{
						string error = $"Could not find any application type matching {applicationName} with version of {version}, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new KeyNotFoundException(error);
					}

					//Add to cache for the following threads
					_ = CachedApplications.Value.AddOrUpdate(cacheKey, application, (key, oldVal) =>
					{
						this.Logger.WriteEntry($"Adding to cache {key}, application {application.ApplicationName}, invocation {invocationId}", LogEntryType.Informational);
						return application;
					});
				}
			}
			return await FunctionEndpointUri(application, invocationId);
		}

		public async Task<string> StatelessEndpointUri(Application applicationInfo, Guid invocationId, bool refreshCache = false)
		{
			if (applicationInfo is null)
			{
				throw new ArgumentNullException(nameof(applicationInfo));
			}
			//Service cache key is pretty simple for stateless services, it's the application name as we only support a single stateless service for a given application
			string cacheKey = applicationInfo.ApplicationName.AbsoluteUri;

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
					if (serviceInfoList == default)
					{
						throw new KeyNotFoundException($"Not a single service was found within the determined application with name {applicationInfo.ApplicationName}, type name of {applicationInfo.ApplicationTypeName} and type version of {applicationInfo.ApplicationTypeVersion}");
					}
					//Find the stateless service within the given application which is the API
					var statelessServices = serviceInfoList.Where(s => s.ServiceKind == System.Fabric.Query.ServiceKind.Stateless && (s.ServiceTypeName.ToUpperInvariant().Contains("API") || s.ServiceTypeName.ToUpperInvariant().Contains("SERVICE"))).ToList();
					if (statelessServices == default || !statelessServices.Any())
					{
						this.Logger.WriteEntry($"Application with name {applicationInfo.ApplicationName} has no services which are stateless and have 'API' in the type name, invocation {invocationId}", LogEntryType.Error);
						throw new KeyNotFoundException($"Application with name {applicationInfo.ApplicationName} has no services which are stateless and have 'API' in the type name");
					}
					if (statelessServices.Count > 1)
					{
						string error = $"{statelessServices.Count} stateless services with 'API' in the name was discovered for application name {applicationInfo.ApplicationName} which is too ambiguous, invocation {invocationId}";
						this.Logger.WriteEntry(error, LogEntryType.Error);
						throw new Exception(error);
					}
					serviceInfo = statelessServices.Single();

					//Add to cache for following threads
					_ = CachedServices.Value.AddOrUpdate(cacheKey, serviceInfo, (key, oldVal) =>
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
					if (serviceInfoList == default)
					{
						throw new KeyNotFoundException($"Not a single service was found within the determined application with name {applicationInfo.ApplicationName}, type name of {applicationInfo.ApplicationTypeName} and type version of {applicationInfo.ApplicationTypeVersion}");
					}

					//Find the stateless service within the given application which is the API
					var statelessServices = serviceInfoList.Where(s => s.ServiceKind == System.Fabric.Query.ServiceKind.Stateless && s.ServiceTypeName.ToUpperInvariant().Contains("FUNCTION")).ToList();
					if (statelessServices == default || !statelessServices.Any())
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
					_ = CachedServices.Value.AddOrUpdate(cacheKey, serviceInfo, (key, oldVal) =>
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

			var cachedServicePartition = await this.Client.ServiceManager.ResolveServicePartitionAsync(service.ServiceName);

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
				address = EndpointJsonToText(endpoint.Address, this.Logger);
			}
			if (address.Contains("0.0.0.0"))
			{
				this.Logger.WriteEntry("Result from Service Fabric contains a local IP address of 0.0.0.0 which is non-routable and will be replaced with loopback of 127.0.0.1", LogEntryType.Warning);
				address = address.Replace("0.0.0.0", "127.0.0.1");
			}
			//If there are multiple, then just return the first(?)
			return address;
		}

		public static string EndpointJsonToText(string json, ILogger logger)
		{
			//The previous JSON deserialize was taking too long so it has been replaced with the below regex with 4x improvements

			//Replace the javascript escapes that seem to come in from SF endpoint object cluster, but not the local test cluster.
			json = json.Replace(@"\/", "/");

			//Workspace used to write regex here:
			//https://regex101.com/r/sTrkU2/1

			//Match on possible protocols - not sure if others need to be added in the beginning?, then domain/host which may have subdomain in dot notation, then optional port number after a colon
			string pattern = @"(http|tcp|https):\/\/([\w_-]+(?:(?:\.?[\w_-]+)+))(:([\d]*))?";
			var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
			if (match.Success)
			{
				//If we match, the URL matched needs to be returned, otherwise we throw an error
				return match.Value;
			}
			else
			{
				string error = $"Found no endpoint for the application in {json}";
				logger.WriteEntry(error, LogEntryType.Error);
				throw new Exception(error);
			}
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
				this.Client.Dispose();
			}
			isDisposed = true;
		}
	}
}
