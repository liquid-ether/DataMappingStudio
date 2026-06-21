using App.Infrastructure.Local;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Local.Tests;

public class SmokeTests
{
    [Fact]
    public void AddLocalStore_builds_a_valid_container()
    {
        using ServiceProvider provider = new ServiceCollection().AddLocalStore().BuildServiceProvider();

        Assert.NotNull(provider);
    }
}
