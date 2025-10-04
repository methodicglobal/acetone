using System;
using System.Text.RegularExpressions;

namespace Methodic.Acetone
{
	/// <summary>
	/// Static utility class for parsing Service Fabric application names from URLs and extracting endpoints from JSON.
	/// This class contains pure algorithms with no external dependencies, making it fully unit testable.
	/// </summary>
	public static class ServiceFabricUrlParser
	{
		private static readonly Regex PullRequestPattern = new Regex(@"^(.+)-(\d+)$", RegexOptions.Compiled);
		// Updated pattern: supports IPv4/hostnames and bracketed IPv6 literal addresses with optional port
		// Examples matched:
		// https://service.com:8080
		// http://localhost
		// https://[::1]:8080
		// https://[2001:db8:85a3::8a2e:370:7334]:8080
		private static readonly Regex EndpointPattern = new Regex(
			@"(?:(?:http|https|tcp):\/\/)(?:\[[0-9a-fA-F:]+\]|[\w_-]+(?:\.[\w_-]+)*)(?::\d+)?",
			RegexOptions.Compiled);

		/// <summary>
		/// Extracts the Service Fabric application name from a URL based on the specified location strategy.
		/// Supports pull request URL patterns where {serviceName}-{pullRequestId} is transformed to {ServiceName}-PR{pullRequestId}.
		/// </summary>
		/// <param name="url">The URL to parse. May or may not include the protocol scheme.</param>
		/// <param name="nameLocation">Specifies where in the URL the application name is located.</param>
		/// <param name="applicationName">Output parameter containing the extracted application name.</param>
		/// <returns>True if the application name was successfully extracted; otherwise false.</returns>
		/// <remarks>
		/// Pull request pattern detection:
		/// - Applies to Subdomain and FirstUrlFragment modes only
		/// - Pattern: {serviceName}-{digits} (e.g., "guard-12906")
		/// - Transforms to: {ServiceName}-PR{digits} (e.g., "Guard-PR12906")
		/// - First letter is capitalized, rest lowercase
		/// 
		/// Examples:
		/// - "https://guard-12906.pav.meth.wtf" (Subdomain) → "Guard-PR12906"
		/// - "https://api.com/guard-12906" (FirstUrlFragment) → "Guard-PR12906"
		/// - "https://guard.pav.meth.wtf" (Subdomain) → "guard" (no transformation)
		/// - "https://my-service.pav.meth.wtf" (Subdomain) → "my-service" (not a PR pattern)
		/// </remarks>
		public static bool TryGetApplicationNameFromUrl(string url, ApplicationNameLocation nameLocation, out string applicationName)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				applicationName = null;
				return false;
			}

			// Ensure URL has a scheme for proper parsing
			if (!url.StartsWith(Uri.UriSchemeHttp, StringComparison.InvariantCultureIgnoreCase) && 
			    !url.StartsWith(Uri.UriSchemeHttps, StringComparison.InvariantCultureIgnoreCase))
			{
				url = Uri.UriSchemeHttps + "://" + url;
			}

			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri originalUri))
			{
				applicationName = url;
				return false;
			}

			string extractedName = ExtractNameByLocation(originalUri, nameLocation);

			if (string.IsNullOrWhiteSpace(extractedName))
			{
				applicationName = null;
				return false;
			}

			// Apply pull request pattern transformation for applicable modes
			if (nameLocation == ApplicationNameLocation.Subdomain || nameLocation == ApplicationNameLocation.FirstUrlFragment)
			{
				if (TryTransformPullRequestPattern(extractedName, out string prApplicationName))
				{
					applicationName = prApplicationName;
					return true;
				}
			}

			// Return extracted name as-is for non-PR URLs
			applicationName = extractedName;
			return true;
		}

		/// <summary>
		/// Extracts the endpoint URL from a Service Fabric endpoint JSON string.
		/// Handles both plain URLs and JSON-formatted endpoint responses.
		/// </summary>
		/// <param name="json">The JSON string or plain URL from the Service Fabric endpoint.</param>
		/// <param name="logger">Logger for diagnostic output.</param>
		/// <returns>The extracted endpoint URL.</returns>
		/// <exception cref="ArgumentNullException">Thrown when json is null or whitespace.</exception>
		/// <exception cref="Exception">Thrown when no valid endpoint is found in the JSON.</exception>
		/// <remarks>
		/// This method handles various endpoint formats returned by Service Fabric:
		/// - Plain URLs: "https://dev-ws-01.methodic.online:5555"
		/// - JSON with unnamed endpoint: {"Endpoints":{"":"https://dev-ws-01.methodic.online:5555"}}
		/// - JSON with named endpoint: {"Endpoints":{"HttpListener":"https://dev-ws-03.methodic.online:999/"}}
		/// - Escaped URLs: {"Endpoints":{"":"https:\/\/dev-ws-04.methodic.online:8899"}}
		/// - IPv6 endpoints: {"Endpoints":{"":"https://[::1]:8080"}}
		/// 
		/// The method will reject remoting endpoints (non-HTTP protocols).
		/// Trailing slashes are automatically removed from URLs.
		/// </remarks>
		public static string EndpointJsonToText(string json, ILogger logger)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				throw new ArgumentNullException(nameof(json), "Endpoint JSON cannot be null or empty");
			}

			// Replace JavaScript escapes that come from Service Fabric endpoint objects
			string normalizedJson = json.Replace(@"\/", "/");

			// Match on protocols (http/https/tcp), domain/host (including IPv6), and optional port
			var match = EndpointPattern.Match(normalizedJson);
			
			if (match.Success)
			{
				string endpoint = match.Value.TrimEnd('/');
				logger?.WriteEntry($"Extracted endpoint: {endpoint}", LogEntryType.Debug);
				return endpoint;
			}

			string error = $"Found no valid HTTP/HTTPS endpoint in: {json}";
			logger?.WriteEntry(error, LogEntryType.Error);
			throw new Exception(error);
		}

		/// <summary>
		/// Validates that a given string appears to be a valid endpoint URL.
		/// </summary>
		/// <param name="endpoint">The endpoint string to validate.</param>
		/// <returns>True if the endpoint appears valid; otherwise false.</returns>
		public static bool IsValidEndpoint(string endpoint)
		{
			if (string.IsNullOrWhiteSpace(endpoint))
			{
				return false;
			}

			return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) && 
			       (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
		}

		/// <summary>
		/// Replaces non-routable IP addresses (like 0.0.0.0 or [::]) with loopback address.
		/// </summary>
		/// <param name="endpoint">The endpoint URL to normalize.</param>
		/// <param name="logger">Optional logger for diagnostic output.</param>
		/// <returns>The normalized endpoint URL.</returns>
		public static string NormalizeLocalEndpoint(string endpoint, ILogger logger = null)
		{
			if (string.IsNullOrWhiteSpace(endpoint))
			{
				return endpoint;
			}

			if (endpoint.Contains("0.0.0.0"))
			{
				logger?.WriteEntry("Endpoint contains non-routable IP 0.0.0.0, replacing with 127.0.0.1", LogEntryType.Warning);
				return endpoint.Replace("0.0.0.0", "127.0.0.1");
			}

			if (endpoint.Contains("[::]"))
			{
				logger?.WriteEntry("Endpoint contains IPv6 any address [::], replacing with [::1]", LogEntryType.Warning);
				return endpoint.Replace("[::]", "[::1]");
			}

			return endpoint;
		}

		#region Private Helper Methods

		private static string ExtractNameByLocation(Uri uri, ApplicationNameLocation nameLocation)
		{
			switch (nameLocation)
			{
				case ApplicationNameLocation.Subdomain:
					// Expects: service.uat.company.com → "service"
					return uri.Host.Split('.')[0];

				case ApplicationNameLocation.SubdomainPreHyphens:
					// Expects: service-uat-01.company.com → "service"
					return uri.Host.Split('.')[0].Split('-')[0];

				case ApplicationNameLocation.SubdomainPostHyphens:
					// Expects: uat-01-service.company.com → "service"
					var parts = uri.Host.Split('.')[0].Split('-');
					return parts[parts.Length - 1];

				case ApplicationNameLocation.FirstUrlFragment:
					// Expects: connect.uat.company.com/service → "service"
					if (uri.Segments.Length > 1)
					{
						return uri.Segments[1].Trim('/', '\\');
					}
					return uri.AbsolutePath.Trim('/', '\\');

				default:
					return uri.Segments.Length > 1 ? uri.Segments[1].Trim('/', '\\') : string.Empty;
			}
		}

		private static bool TryTransformPullRequestPattern(string name, out string transformedName)
		{
			if (string.IsNullOrWhiteSpace(name) || !name.Contains("-"))
			{
				transformedName = null;
				return false;
			}

			var match = PullRequestPattern.Match(name);
			if (match.Success)
			{
				string serviceName = match.Groups[1].Value;
				string prNumber = match.Groups[2].Value;

				// Capitalize first letter, lowercase the rest
				string capitalizedServiceName = char.ToUpper(serviceName[0]) + serviceName.Substring(1).ToLower();
				transformedName = $"{capitalizedServiceName}-PR{prNumber}";
				return true;
			}

			transformedName = null;
			return false;
		}

		#endregion
	}
}
