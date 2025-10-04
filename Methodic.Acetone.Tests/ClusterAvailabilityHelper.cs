using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Methodic.Acetone.Tests
{
	/// <summary>
	/// Helper class to detect if a Service Fabric cluster is available for testing.
	/// This allows tests to run against a real cluster when available, or use mock data when not.
	/// </summary>
	public static class ClusterAvailabilityHelper
	{
		private static bool? _isClusterAvailable;
		private static readonly object _lock = new object();

		/// <summary>
		/// Checks if a Service Fabric cluster is available at the specified endpoint.
		/// Results are cached for the duration of the test run.
		/// </summary>
		public static bool IsClusterAvailable(string clusterEndpoint = "localhost", int port = 19000, int timeoutMs = 1000)
		{
			lock (_lock)
			{
				if (_isClusterAvailable.HasValue)
				{
					return _isClusterAvailable.Value;
				}

				_isClusterAvailable = CheckClusterConnection(clusterEndpoint, port, timeoutMs);
				return _isClusterAvailable.Value;
			}
		}

		/// <summary>
		/// Attempts to connect to the Service Fabric cluster endpoint.
		/// Returns true if connection succeeds, false otherwise.
		/// </summary>
		private static bool CheckClusterConnection(string host, int port, int timeoutMs)
		{
			try
			{
				using (var client = new TcpClient())
				{
					var result = client.BeginConnect(host, port, null, null);
					var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));
					
					if (!success)
					{
						return false;
					}

					client.EndConnect(result);
					return client.Connected;
				}
			}
			catch (SocketException)
			{
				return false;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Resets the cached cluster availability state.
		/// Useful for testing or when cluster state may have changed.
		/// </summary>
		public static void ResetCache()
		{
			lock (_lock)
			{
				_isClusterAvailable = null;
			}
		}

		/// <summary>
		/// Gets a user-friendly message about cluster availability for test output.
		/// </summary>
		public static string GetAvailabilityMessage()
		{
			bool isAvailable = IsClusterAvailable();
			return isAvailable
				? "✓ Service Fabric cluster detected - running tests against real cluster"
				: "ℹ Service Fabric cluster not available - using mock data";
		}
	}
}
