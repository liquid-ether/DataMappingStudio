using App.Application.Abstractions;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace App.Web.Workspaces;

/// <summary>
/// Marks a user's workspace as having a live circuit for the duration of that circuit, so
/// <see cref="WorkspaceRegistry.SweepIdle"/> won't evict it mid-session. Scoped (one per circuit); resolves
/// the same <see cref="ICurrentUser"/> the accessor uses, so the key matches the registry's.
/// </summary>
internal sealed class WorkspaceCircuitHandler(ICurrentUser user, WorkspaceLiveness liveness) : CircuitHandler
{
    private string? _key;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _key = WorkspaceRegistry.Sanitize(user.Name);
        liveness.Enter(_key);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (_key is not null)
        {
            liveness.Leave(_key);
        }

        return Task.CompletedTask;
    }
}
