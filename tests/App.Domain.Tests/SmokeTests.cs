using App.Domain;

namespace App.Domain.Tests;

public class SmokeTests
{
    [Fact]
    public void Domain_assembly_loads()
    {
        Assert.Equal("App.Domain", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
