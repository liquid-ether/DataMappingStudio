using App.Application.Abstractions;
using App.Domain.Data;

namespace App.Application.Sync;

/// <summary>
/// Host-wide cache of the folded remote state, keyed by <see cref="IRemoteStore.RemoteVersion"/>. On a
/// multi-user host every workspace refreshes against the same shared folder; without this each of N
/// workspaces re-folds every writer log after any publish (N identical folds per cycle). Consumers must
/// treat the returned <see cref="FoldedState"/> as read-only (refresh only reads it).
/// </summary>
public sealed class RemoteFoldCache
{
    private readonly object _gate = new();
    private string? _version;
    private FoldedState? _state;

    public FoldedState GetOrFold(IRemoteStore remote, string version)
    {
        lock (_gate)
        {
            if (version == _version && _state is not null)
            {
                return _state;
            }
        }

        // Fold outside the lock (it reads every log file); a concurrent duplicate fold is harmless.
        FoldedState folded = ChangeFold.Fold(remote.ReadAllChanges());
        lock (_gate)
        {
            _version = version;
            _state = folded;
        }

        return folded;
    }
}
