using System.Collections.Concurrent;

namespace App.Web.Workspaces;

/// <summary>
/// Counts the live Blazor Server circuits each user currently has open. The per-user DB-bound services are
/// resolved once per circuit and cached for its lifetime, so the registry must never evict a workspace out
/// from under an active session (that would dispose the SQLite connection the open circuit still holds).
/// Keyed by the same sanitized user key the registry uses.
/// </summary>
public sealed class WorkspaceLiveness
{
    private readonly ConcurrentDictionary<string, int> _open = new(StringComparer.OrdinalIgnoreCase);

    public void Enter(string key) => _open.AddOrUpdate(key, 1, (_, n) => n + 1);

    public void Leave(string key)
    {
        // Decrement (floored at 0); drop the entry only while it is genuinely zero (atomic, so a concurrent
        // Enter that raced in is not lost).
        if (_open.AddOrUpdate(key, 0, (_, n) => Math.Max(0, n - 1)) == 0)
        {
            _open.TryRemove(new KeyValuePair<string, int>(key, 0));
        }
    }

    public bool IsActive(string key) => _open.TryGetValue(key, out int n) && n > 0;
}
