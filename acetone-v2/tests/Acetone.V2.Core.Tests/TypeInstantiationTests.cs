using System.Fabric.Query;
using Xunit;

namespace Acetone.V2.Core.Tests;

public class TypeInstantiationTests
{
    [Fact]
    public void CanInstantiateApplication()
    {
        // Application has no public constructor. This will fail to compile if I try new Application()
        // But maybe I can use FormatterServices.GetUninitializedObject or reflection?
        // Or maybe I should wrap the result types.
    }
}
