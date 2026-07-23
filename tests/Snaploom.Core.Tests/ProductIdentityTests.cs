using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ProductIdentityTests
{
    [Fact]
    public void ProductIdentifiersMatchThePublishedSpecification()
    {
        Assert.Equal("Snaploom", ProductIdentity.Name);
        Assert.Equal("Snaploom", ProductIdentity.RootNamespace);
        Assert.Equal("Snaploom.Desktop", ProductIdentity.WindowsAppId);
        Assert.Equal("com.snaploom.app", ProductIdentity.MacOSBundleId);
        Assert.Equal("snaploom", ProductIdentity.ReleasePrefix);
        Assert.Equal("Snaploom", ProductIdentity.ConfigurationDirectoryName);
    }
}
