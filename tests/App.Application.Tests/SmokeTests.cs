using App.Application;
using App.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace App.Application.Tests;

public class SmokeTests
{
    [Fact]
    public void AddApplication_registers_resolvable_services()
    {
        using ServiceProvider provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        IClock clock = provider.GetRequiredService<IClock>();

        Assert.IsType<SystemClock>(clock);
        Assert.True(clock.UtcNow <= DateTimeOffset.UtcNow.AddSeconds(1));
    }
}
