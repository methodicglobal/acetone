using System;
using System.Threading.Tasks;

namespace Methodic.Acetone
{
	public interface IServiceUrlResolver
	{
		Task<string> ResolveServiceUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false);
		Task<string> ResolveFunctionUri(string applicationName, Guid invocationId, string version = null, bool refreshCache = false);
	}
}
