using App.Application.Abstractions;

namespace App.Web.Workspaces;

/// <summary>Resolves the current request/circuit's <see cref="Workspace"/> (scoped).</summary>
public interface IWorkspaceAccessor
{
    Workspace Current { get; }
}

/// <summary>
/// Scoped accessor: maps the current <see cref="ICurrentUser"/> to their workspace via the registry,
/// caching the lookup for the lifetime of the circuit/request.
/// </summary>
public sealed class WorkspaceAccessor(IWorkspaceRegistry registry, ICurrentUser user) : IWorkspaceAccessor
{
    private Workspace? _current;

    public Workspace Current => _current ??= registry.Get(user.Name);
}
