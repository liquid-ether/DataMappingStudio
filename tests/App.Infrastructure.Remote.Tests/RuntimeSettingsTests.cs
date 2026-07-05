using App.Application.Abstractions;
using App.Application.Configuration;
using App.Domain.Catalog;

namespace App.Infrastructure.Remote.Tests;

public sealed class RuntimeSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dms-settings-" + Guid.NewGuid().ToString("N"));

    public RuntimeSettingsTests() => Directory.CreateDirectory(_dir);

    private sealed class AdminUser : ICurrentUser
    {
        public string UserId => "admin";
        public string Name => "admin";
        public string DisplayName => "admin";
        public IReadOnlyCollection<string> Roles => ["Administrator"];
        public bool HasPermission(string permission) => true;
    }

    private sealed class ReaderUser : ICurrentUser
    {
        public string UserId => "reader";
        public string Name => "reader";
        public string DisplayName => "reader";
        public IReadOnlyCollection<string> Roles => ["Reader"];
        public bool HasPermission(string permission) => false;
    }

    [Fact]
    public void Shared_settings_round_trip_and_converge_across_hosts()
    {
        FileSettingsStore storeA = new(_dir);
        RuntimeConfigService hostA = new(storeA);
        RuntimeConfigService hostB = new(new FileSettingsStore(_dir)); // second host, same folder

        Assert.Equal("30", hostA.Get(SettingsRegistry.AutoRefreshSeconds)); // registry default

        hostA.Set(new AppConfigEntry { Key = SettingsRegistry.AutoRefreshSeconds, Value = "60", Scope = ConfigScope.Shared }, new AdminUser());

        Assert.Equal(60, hostA.GetInt(SettingsRegistry.AutoRefreshSeconds, 30));
        Assert.Equal(60, hostB.GetInt(SettingsRegistry.AutoRefreshSeconds, 30)); // converges via the document
        Assert.Equal("admin", storeA.Read()!.UpdatedBy);
    }

    [Fact]
    public void Values_are_validated_and_permission_checked()
    {
        RuntimeConfigService config = new(new FileSettingsStore(_dir));

        Assert.Throws<ArgumentException>(() => config.Set(
            new AppConfigEntry { Key = SettingsRegistry.AutoRefreshSeconds, Value = "not-a-number", Scope = ConfigScope.Shared }, new AdminUser()));
        Assert.Throws<ArgumentException>(() => config.Set(
            new AppConfigEntry { Key = SettingsRegistry.AutoRefreshSeconds, Value = "2", Scope = ConfigScope.Shared }, new AdminUser())); // below Min
        Assert.Throws<ArgumentException>(() => config.Set(
            new AppConfigEntry { Key = "Not.A.Setting", Value = "1", Scope = ConfigScope.Shared }, new AdminUser()));
        Assert.Throws<UnauthorizedAccessException>(() => config.Set(
            new AppConfigEntry { Key = SettingsRegistry.AutoRefreshSeconds, Value = "60", Scope = ConfigScope.Shared }, new ReaderUser()));
    }

    [Fact]
    public void Without_a_shared_store_defaults_apply_and_shared_writes_fail_cleanly()
    {
        RuntimeConfigService config = new(shared: null, localFilePath: Path.Combine(_dir, "local.json"));

        Assert.Equal("en", config.Get(SettingsRegistry.DefaultLanguage));
        Assert.Throws<InvalidOperationException>(() => config.Set(
            new AppConfigEntry { Key = SettingsRegistry.DefaultLanguage, Value = "fr", Scope = ConfigScope.Shared }, new AdminUser()));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
