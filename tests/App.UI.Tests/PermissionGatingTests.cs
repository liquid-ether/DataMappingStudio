using App.Application.Abstractions;
using App.Application.Security;
using App.UI.Components;
using App.UI.Localization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI.Tests;

/// <summary>The UI honours permissions: controls for an operation appear only when the user is granted it.</summary>
public class PermissionGatingTests : AppTestContext
{
    private sealed class FixedUser(params string[] permissions) : ICurrentUser
    {
        private readonly HashSet<string> _permissions = new(permissions, StringComparer.Ordinal);

        public string UserId => "u";
        public string Name => "u";
        public string DisplayName => "u";
        public IReadOnlyCollection<string> Roles => ["Reader"];
        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }

    public PermissionGatingTests() => Services.AddSingleton<LanguageState>();

    [Fact]
    public void Top_bar_hides_publish_without_the_publish_permission()
    {
        Services.AddSingleton<ICurrentUser>(new FixedUser(Permissions.DataView));

        IRenderedComponent<MsTopBar> cut = Render<MsTopBar>();

        Assert.Empty(cut.FindAll("button.ms-publish"));
    }

    [Fact]
    public void Top_bar_shows_publish_with_the_publish_permission()
    {
        Services.AddSingleton<ICurrentUser>(new FixedUser(Permissions.DataView, Permissions.DataPublish));

        IRenderedComponent<MsTopBar> cut = Render<MsTopBar>();

        Assert.NotEmpty(cut.FindAll("button.ms-publish"));
    }
}
