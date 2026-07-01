namespace App.Application.Security;

/// <summary>
/// The built-in ("system") roles seeded on first run and their permission grants. System roles cannot be
/// deleted; admins may create additional custom roles with any subset of <see cref="Permissions.All"/>.
/// </summary>
public static class SecurityRoles
{
    public const string Administrator = "Administrator";
    public const string Publisher = "Publisher";
    public const string Editor = "Editor";
    public const string Reader = "Reader";

    public static readonly IReadOnlyList<string> All = [Administrator, Publisher, Editor, Reader];

    /// <summary>Seed permission grant per system role.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SystemRolePermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Administrator] = Permissions.All,
            [Publisher] =
            [
                Permissions.DataView, Permissions.DataEdit, Permissions.DataPublish, Permissions.DataImport,
                Permissions.MappingsManage, Permissions.LineageView, Permissions.HistoryView,
            ],
            [Editor] =
            [
                Permissions.DataView, Permissions.DataEdit,
                Permissions.MappingsManage, Permissions.LineageView, Permissions.HistoryView,
            ],
            [Reader] = [Permissions.DataView, Permissions.LineageView, Permissions.HistoryView],
        };

    public static bool IsSystem(string role) => All.Contains(role, StringComparer.Ordinal);
}
