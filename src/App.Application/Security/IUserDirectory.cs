namespace App.Application.Security;

/// <summary>A user as shown in the admin list.</summary>
public sealed record UserSummary(
    string Id,
    string UserName,
    string? DisplayName,
    string? Email,
    bool IsEnabled,
    bool IsLockedOut,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginUtc,
    string Origin);

/// <summary>Request to create a local user.</summary>
public sealed record CreateUserRequest(
    string UserName,
    string? Email,
    string? DisplayName,
    string Password,
    IReadOnlyList<string> Roles);

/// <summary>A role and its permission grant.</summary>
public sealed record RoleSummary(
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int MemberCount);

/// <summary>Outcome of an admin mutation, carrying provider error messages for the UI.</summary>
public sealed record OperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static readonly OperationResult Ok = new(true, []);

    public static OperationResult Fail(params string[] errors) => new(false, errors);
}

/// <summary>
/// Admin surface over the security store: user + role management for the admin UI. Concrete
/// implementation lives in <c>App.Infrastructure.Identity</c> over ASP.NET Core Identity.
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(string? search = null, CancellationToken ct = default);

    Task<OperationResult> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<OperationResult> SetEnabledAsync(string userId, bool enabled, CancellationToken ct = default);

    Task<OperationResult> SetRolesAsync(string userId, IReadOnlyList<string> roles, CancellationToken ct = default);

    Task<OperationResult> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default);

    Task<OperationResult> UnlockAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken ct = default);

    Task<OperationResult> CreateRoleAsync(string name, string? description, IReadOnlyList<string> permissions, CancellationToken ct = default);

    Task<OperationResult> UpdateRolePermissionsAsync(string name, IReadOnlyList<string> permissions, CancellationToken ct = default);

    Task<OperationResult> DeleteRoleAsync(string name, CancellationToken ct = default);
}
