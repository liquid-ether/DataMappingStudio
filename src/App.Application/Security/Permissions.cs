namespace App.Application.Security;

/// <summary>
/// The fine-grained operations the app authorizes. Each is carried on the authenticated principal as a
/// <see cref="ClaimType"/> claim (expanded from the user's roles at sign-in), gated in the UI via
/// <see cref="Abstractions.ICurrentUser.HasPermission"/> and on the server via a policy per permission.
/// </summary>
public static class Permissions
{
    /// <summary>Claim type under which granted permissions ride on the principal.</summary>
    public const string ClaimType = "permission";

    public const string DataView = "Data.View";
    public const string DataEdit = "Data.Edit";
    public const string DataPublish = "Data.Publish";
    public const string DataImport = "Data.Import";
    public const string MappingsManage = "Mappings.Manage";
    public const string LineageView = "Lineage.View";
    public const string HistoryView = "History.View";
    public const string UsersManage = "Users.Manage";
    public const string RolesManage = "Roles.Manage";
    public const string SecurityConfigure = "Security.Configure";
    public const string AppConfigure = "App.Configure";

    /// <summary>Every permission — the Administrator grant and the source of the authorization policies.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        DataView, DataEdit, DataPublish, DataImport,
        MappingsManage, LineageView, HistoryView,
        UsersManage, RolesManage, SecurityConfigure, AppConfigure,
    ];
}
