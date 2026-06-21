using App.Infrastructure.Remote;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Remote.Tests;

public class SmokeTests
{
    [Fact]
    public void AddRemoteStore_builds_a_valid_container()
    {
        using ServiceProvider provider = new ServiceCollection().AddRemoteStore().BuildServiceProvider();

        Assert.NotNull(provider);
    }
}
