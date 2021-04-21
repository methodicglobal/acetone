using Microsoft.Web.Iis.Rewrite;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Methodic.Acetone
{
	public class ServiceFabricLocator : IRewriteProvider, IProviderDescriptor
	{
		private const string EventLogName = "Methodic.Acetone";

		public IRewriteContext RewriteContext
		{ get; set; }

		public IServiceUrlResolver ServiceUrlResolver
		{ get; set; }

		public string VersionParameter
		{ get; set; }

		public List<string> ClusterConnectionStrings
		{ get; set; }

		public string ClearCacheParameter
		{ get; set; }

		public string ClientCertificateThumbprint
		{ get; set; }

		public List<string> ServerCertificateThumbprints
		{ get; set; }

		public string ClientCertificateSubjectDistinguishedName
		{ get; set; }

		public string ClientCertificateIssuerDistinguishedName
		{ get; set; }

		public List<string> ServerCertificateCommonNames
		{ get; set; }

		public CredentialsType CredentialsType
		{ get; set; } = CredentialsType.CertificateThumbprint;

		public ApplicationNameLocation ApplicationNameLocation
		{ get; set; } = ApplicationNameLocation.Subdomain;
		public ILogger Logger { get; }

		public ServiceFabricLocator(ILogger logger)
		{
			this.Logger = logger;
		}

		public ServiceFabricLocator()
		{
			this.Logger = new EventLogger(EventLogName, false);
		}

		public IEnumerable<SettingDescriptor> GetSettings()
		{
			var settings = new List<SettingDescriptor>
			{
				new SettingDescriptor("EnableLogging", "Enable Logging (True or False)", "Defaults to False. True or False for logging messages into the event log"),

				new SettingDescriptor("ClusterConnectionStrings", "Cluster Connection Strings (comma separated)", "Connection string for the service fabric cluster to discover. Can be a comma-separated list for multiple endpoints. eg my-cluster-ss-lb.methodic.com:66042"),
				new SettingDescriptor("ApplicationNameLocation", "Application Name Location (Subdomain, SubdomainPostHyphens, SubdomainPreHyphens or FirstUrlFragment)", "Defaults to Subdomain. One of the following options: Subdomain, SubdomainPostHyphens, SubdomainPreHyphens or FirstUrlFragment. Defaults to Subdomain if not supplied, or value cannot be parsed. The name of the application/service is derived from: Subdomain: the very subdomain eg https://mycoolservice.methodic.com, or; SubdomainPostHyphens: the subdomain after the last hyphen, eg https://uat-mycoolservice.methodic.com, or; SubdomainPreHyphens: the subdomain before the first hyphen, eg https://uat-mycoolservice.methodic.com, or; FirstUrlFragment: the first URL segment eg https://uat.methodic.com/mycoolservice"),

				new SettingDescriptor("PartitionCacheLimit", $"Partition Cache Limit (optional, default: {ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit})", "The maximum number of cached location entries on the client"),
				new SettingDescriptor("VersionParameter", "Version Query String Parameter Name", "(coming soon) Name of query string parameter which contains the version number of the application to locate (eg methodic-api-version used as https://mycoolservice.methodic.com/items?methodic-api-version=v1.2.3.4)"),
				new SettingDescriptor("ClearCacheParameter", "Clear Cache Query String Parameter Name", "(coming soon) If the query string parameter parses as a True boolean then all cached entries for Service Fabric are ignored and gets the application and services information from the cluster once again. Slower operation and should only be used for diagnostics (eg no-cache used as https://mycoolservice.methodic.com/items?no-cache=true)"),

				new SettingDescriptor("CredentialsType", "Credentials Type (Local, CertificateThumbprint or CertificateCommonName)", "Local, CertificateThumbprint (default) or CertificateCommonName - determine whether to use no credentials (local), or Thumbprint/CommonName to locale certificate in LocalMachine>My"),
				new SettingDescriptor("ClientCertificateThumbprint", "Client Certificate Thumbprint", "(Required for secure communication) Certificate thumbprint of the local client certificate - needs to be installed in LocalMachine.My and the IIS process must have permissions to read the private key"),
				new SettingDescriptor("ServerCertificateThumbprints", "Server Certificate Thumbprint (Optional, Comma Separated)", "(Required for secure communication) Certificate thumbprint of the certificate installed on the cluster as ServerCertificate -> see cluster manifest"),
				
				
				new SettingDescriptor("ClientCertificateSubjectDistinguishedName", "Client Certificate Subject Distinguished Name", "Certificate CN=User name for connecting to the cluster"),
				new SettingDescriptor("ClientCertificateIssuerDistinguishedName", "Client Certificate Issuer Distinguished Name", "Certificate issuer distinguished name for connecting to the cluster. eg CN=Sectigo RSA Domain Validation Secure Server CA, O=Sectigo Limited, L=Salford, S=Greater Manchester, C=GB"),
				new SettingDescriptor("ServerCertificateCommonNames", "Server/Remote Common Names (Optional, Comma Separated)", "Comma separated list of Common Names of the remote cluster certificate. eg CN=*.mycluster.prod.com,CN=*.mycluster.dr.com")
			};
			return settings;
		}

		public void Initialize(IDictionary<string, string> settings, IRewriteContext rewriteContext)
		{
			//Rewrite Context
			this.RewriteContext = rewriteContext;
			this.RewriteContext.RewriteCacheEnabled = false;
			ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit = 5;
			//Check if information log is turned on
			if (settings.TryGetValue("EnableLogging", out string loginfoParameter))
			{
				this.Logger.Enabled = bool.Parse(loginfoParameter);
			}
			else
			{
				this.Logger.Enabled = false;
				System.Diagnostics.Debug.WriteLine($"Acetone IIS rewrite module Initialize called with an invalid 'LogInformation' parameter. Expected 'True' or 'False', however configured with {loginfoParameter ?? "|NULL|"}. Defaulting to 'False' and not logging any further");

			}
			this.Logger.WriteEntry($"Methodic Acetone is initializing with logging {(this.Logger.Enabled ? "enabled" : "disabled")}", LogEntryType.Informational);

			//Cluster connection string
			if (!settings.TryGetValue("ClusterConnectionStrings", out string clusterConnectionString))
			{
				string error = "ClusterConnectionStrings was not set in IIS settings and is required to connect to the service fabric cluster for node URL rewrite. eg test-sf-cluster:19000";
				this.Logger.WriteEntry(error, LogEntryType.Error, 0422);
				throw new ArgumentException(error, "ClusterConnectionStrings");
			}
			this.ClusterConnectionStrings = clusterConnectionString?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)?.Select(a =>
			{
				if (!string.IsNullOrEmpty(a))
				{
					return a.Trim();
				}
				return null;
			})?.ToList();

			if (settings.TryGetValue("CredentialsType", out string credentialsType))
			{
				if (Enum.TryParse(credentialsType, out CredentialsType result))
				{
					this.CredentialsType = result;
				}
				else
				{
					//Couldn't parse enum, leave at default and warn
					this.Logger.WriteEntry($"CredentialsType parameter of {credentialsType} has been provided for connecting to {(this.ClusterConnectionStrings == null ? "NULL" : string.Join(", ", this.ClusterConnectionStrings))} but could not be parsed into Local, CertificateThumbprint or CertificateCommonName- defaulting to {this.CredentialsType}", LogEntryType.Warning);
				}
			}
			else
			{
				//leave at default, but warn
				this.Logger.WriteEntry($"No CredentialsType parameter has been provided for connecting to {(this.ClusterConnectionStrings == null ? "NULL" : string.Join(", ", this.ClusterConnectionStrings))} - defaulting to {this.CredentialsType}. Other options include CertificateCommonName or Local for testing", LogEntryType.Warning);
			}

			if (this.CredentialsType == CredentialsType.CertificateThumbprint)
			{
				//Client Certificate
				if (!settings.TryGetValue("ClientCertificateThumbprint", out string clientCertificateThumbprint))
				{
					string error = "ClientCertificateThumbprint was not set in IIS settings and is required to connect to the service fabric cluster. eg 433f952929a7a637ee02d5a2644189937e636b2e.";
					this.Logger.WriteEntry(error, LogEntryType.Error);
					throw new ArgumentException(error, "ClientCertificateThumbprint");
				}
				this.ClientCertificateThumbprint = clientCertificateThumbprint;


				//Server Certificate
				if (!settings.TryGetValue("ServerCertificateThumbprints", out string serverCertificateThumbprint))
				{
					string error = "ServerCertificateThumbprint (aka Remote Certificate Thumbprint) was not set in IIS settings and might be required to connect to the service fabric cluster. eg 433f952929a7a637ee02d5a2644189937e636b2e.";
					this.Logger.WriteEntry(error, LogEntryType.Warning);
				}
				this.ServerCertificateThumbprints = serverCertificateThumbprint?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)?.Select(a =>
				{
					if (!string.IsNullOrEmpty(a))
					{
						return a.Trim();
					}
					return null;
				})?.ToList();
			}
			else if (this.CredentialsType == CredentialsType.CertificateCommonName)
			{
				if (!settings.TryGetValue("ClientCertificateSubjectDistinguishedName", out string clientCertSDN))
				{
					string error = "ClientCertificateSubjectDistinguishedName was not set in IIS settings and is required to connect to the service fabric cluster. eg E=cluster.access@company.com, CN=Cluster Access, CN=Users, DC=company, DC=com";
					this.Logger.WriteEntry(error, LogEntryType.Error);
					throw new ArgumentException(error, "ClientCertificateSubjectDistinguishedName");
				}
				this.ClientCertificateSubjectDistinguishedName = clientCertSDN;

				if (!settings.TryGetValue("ClientCertificateIssuerDistinguishedName", out string clientCertIDN))
				{
					string error = "ClientCertificateIssuerDistinguishedName was not set in IIS settings and is required to connect to the service fabric cluster. eg CN=company-authority-issuer, DC=company, DC=com";
					this.Logger.WriteEntry(error, LogEntryType.Error);
					throw new ArgumentException(error, "ClientCertificateIssuerDistinguishedName");
				}
				this.ClientCertificateIssuerDistinguishedName = clientCertIDN;

				if (!settings.TryGetValue("ServerCertificateCommonNames", out string serverCertificateCommonNames))
				{
					string error = "ServerCertificateCommonNames (aka Remote Certificate Common Names) was not set in IIS settings and might be required to connect to the service fabric cluster. eg *.mycluster.prod.com,CN=*.mycluster.dr.com";
					this.Logger.WriteEntry(error, LogEntryType.Warning);
				}

				this.ServerCertificateCommonNames = serverCertificateCommonNames?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)?.Select(a =>
				{
					if (!string.IsNullOrEmpty(a))
					{
						return a.Trim();
					}
					return null;
				})?.ToList();
			}


			//If there's a version query parameter name supplied, save it to a property
			if (settings.TryGetValue("VersionParameter", out string versionParameter))
			{
				this.VersionParameter = versionParameter;
			}
			//If there's a clear cache query parameter name supplied, save that to a property to
			if (settings.TryGetValue("ClearCacheParameter", out string clearCacheParameter))
			{
				this.ClearCacheParameter = clearCacheParameter;
			}
			//If there's a partition cache limit provided, update the settings. Warn if cannot parse, or no value was provided at all
			if (settings.TryGetValue("PartitionCacheLimit", out string partitionCacheLimit))
			{
				if (long.TryParse(partitionCacheLimit, out long pcl))
				{
					ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit = pcl;
				}
				else
				{
					this.Logger.WriteEntry($"Error parsing parameter PartitionCacheLimit into a number from value {partitionCacheLimit}, leaving default partition cache limit at {ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit}", LogEntryType.Warning);
				}
			}
			else
			{
				this.Logger.WriteEntry($"No value for PartitionCacheLimit was provided, therefore a default value of {ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit} will be used", LogEntryType.Warning);
			}


			//If there's a application name location supplied for deriving application from URL then hold it for the rewrite
			if (settings.TryGetValue("ApplicationNameLocation", out string applicationNameLocation))
			{
				if (Enum.TryParse(applicationNameLocation, out ApplicationNameLocation result))
				{
					this.ApplicationNameLocation = result;
				}
				else
				{
					//If we can't parse the string to enum, default it to Subdomain (should already be set, but just in case)
					this.ApplicationNameLocation = ApplicationNameLocation.Subdomain;
					this.Logger.WriteEntry($"Supplied value of {applicationNameLocation} could not be parsed as the appropriate Enum values of Subdomain, SubdomainWithEnvironment or FirstUrlFragment. Please only supply the available options. Defaulting to {this.ApplicationNameLocation}", LogEntryType.Warning);
				}
			}
			else
			{
				//Defaults to Subdomain if there's no value provided
				this.ApplicationNameLocation = ApplicationNameLocation.Subdomain;
			}





			try
			{
				//Construct the SF URL resolver and write info event with configuration details
				switch (this.CredentialsType)
				{
					default:
					case CredentialsType.CertificateThumbprint:
						this.Logger.WriteEntry($"Acetone configured with the following values - Cluster Connection String: {(this.ClusterConnectionStrings == null ? "NULL" : string.Join(", ", this.ClusterConnectionStrings)) }, Credentials Type: {this.CredentialsType}, Client Certificate Thumbprint: {this.ClientCertificateThumbprint}, Server Certificate Thumbprint: {(this.ServerCertificateThumbprints == null ? "NULL" : string.Join(", ", this.ServerCertificateThumbprints))}, Application Name Location: {this.ApplicationNameLocation}, Log Information: {this.Logger.Enabled}, Version Query String Parameter Name: {this.VersionParameter}, Clear Cache Query String Parameter Name: {this.ClearCacheParameter}, Service Fabric Client Partition Cache Limit: {ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit}", LogEntryType.Informational);
						this.ServiceUrlResolver = new ServiceFabricUrlResolver(this.Logger, this.ClusterConnectionStrings, this.ClientCertificateThumbprint, this.ServerCertificateThumbprints);
						break;
					case CredentialsType.CertificateCommonName:
						this.Logger.WriteEntry($"Acetone configured with the following values - Cluster Connection String: {(this.ClusterConnectionStrings == null ? "NULL" : string.Join(", ", this.ClusterConnectionStrings))}, Credentials Type: {this.CredentialsType}, Client Certificate Subject Distinguished Name: {this.ClientCertificateSubjectDistinguishedName}, Client Certificate Issuer Distinguished Name: {this.ClientCertificateIssuerDistinguishedName}, ServerCertificateCommonNames: {(this.ServerCertificateCommonNames == null ? "NULL" : string.Join(", ", this.ServerCertificateCommonNames))}, Application Name Location: {this.ApplicationNameLocation}, Log Information: {this.Logger.Enabled}, Version Query String Parameter Name: {this.VersionParameter}, Clear Cache Query String Parameter Name: {this.ClearCacheParameter}, Service Fabric Client Partition Cache Limit: {ServiceFabricUrlResolver.Settings.PartitionLocationCacheLimit}", LogEntryType.Informational);
						this.ServiceUrlResolver = new ServiceFabricUrlResolver(this.Logger, this.ClusterConnectionStrings, this.ClientCertificateSubjectDistinguishedName, this.ClientCertificateIssuerDistinguishedName, this.ServerCertificateCommonNames);
						break;
					case CredentialsType.Local:
						this.ServiceUrlResolver = new ServiceFabricUrlResolver(this.Logger, this.ClusterConnectionStrings);
						break;
				}

			}
			catch (Exception ex)
			{
				string error = $"Acetone IIS Rewrite extension experienced a fatal error constructing a service fabric client using Cluster Connection String: {string.Join(", ", this.ClusterConnectionStrings) ?? "NULL"}. Error: {FormatException(ex)}";
				this.Logger.WriteEntry(error, LogEntryType.Error);
				throw new Exception(error, ex);
			}

			this.Logger.WriteEntry("Methodic Acetone initialized successfully. Ready to rewrite requests.", LogEntryType.Informational);

		}

		public string Rewrite(string value)
		{
			Guid invocationId = Guid.NewGuid();
			var stopwatch = new System.Diagnostics.Stopwatch();
			stopwatch.Start();

			string result;


			this.Logger.WriteEntry($"Methodic Acetone Rewrite called for url {value}, invocation {invocationId}", LogEntryType.Informational);

			if (string.IsNullOrWhiteSpace(value))
			{
				string message = $"Critical error! Rewrite called with an empty URL. Please ensure the IIS URL rewrite rule is configured to pass the original request URL to the Acetone provider. Invocation {invocationId}";
				this.Logger.WriteEntry(message, LogEntryType.Error);
				throw new ArgumentNullException("Provided URL", message);
			}
			if (!ServiceFabricUrlResolver.TryGetApplicationNameFromUrl(value, this.ApplicationNameLocation, out string applicationName))
			{
				string message = $"Supplied URL of {value} has no resolvable application name";
				this.Logger.WriteEntry(message, LogEntryType.Error);
				throw new ArgumentException(message, "Provided URL");
			}


			this.Logger.WriteEntry($"Determined application name to be {applicationName}, invocation {invocationId}", LogEntryType.Informational);


			if (value.ToLowerInvariant().Contains("/function/"))
			{
				result = this.ServiceUrlResolver.ResolveFunctionUri(applicationName, invocationId).GetAwaiter().GetResult();

				this.Logger.WriteEntry($"Final result returning is {result}, invocation {invocationId}", LogEntryType.Informational);

			}
			else
			{
				result = this.ServiceUrlResolver.ResolveServiceUri(applicationName, invocationId).GetAwaiter().GetResult();
				this.Logger.WriteEntry($"Final result returning is {result}, invocation {invocationId}", LogEntryType.Informational);
			}

			stopwatch.Stop();
			this.Logger.WriteEntry($"Time taken was {stopwatch.ElapsedMilliseconds} ms, invocation {invocationId}", LogEntryType.Informational);


			return result;
		}

		public static string FormatException(Exception ex)
		{
			StringBuilder error = new StringBuilder();
			WriteExceptionDetails(ex, error);
			return error.ToString();
		}

		private static void WriteExceptionDetails(Exception exception, StringBuilder builderToFill, int level = 0)
		{
			if (exception == default || builderToFill == default)
			{
				return;
			}
			var indent = new string(' ', level);

			if (level > 0)
			{
				_ = builderToFill.AppendLine(indent + "=== INNER EXCEPTION ===");
			}

			void append(string prop)
			{
				var propInfo = exception.GetType()?.GetProperty(prop);
				var val = propInfo?.GetValue(exception);

				if (val != default)
				{
					_ = builderToFill.AppendFormat("{0}{1}: {2}{3}", indent, prop, val.ToString(), Environment.NewLine);
				}
			}

			append("Message");
			append("HResult");
			append("HelpLink");
			append("Source");
			append("StackTrace");
			append("TargetSite");

			foreach (DictionaryEntry de in exception.Data)
			{
				_ = builderToFill.AppendFormat("{0} {1} = {2}{3}", indent, de.Key, de.Value, Environment.NewLine);
			}

			if (exception.InnerException != default)
			{
				WriteExceptionDetails(exception.InnerException, builderToFill, ++level);
			}
		}
	}
}
