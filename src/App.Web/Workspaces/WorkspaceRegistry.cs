using System.Collections.Concurrent;

namespace App.Web.Workspaces;

/// <summary>Owns the per-user <see cref="Workspace"/> cache for one host.</summary>
public interface IWorkspaceRegistry
{
    /// <summary>The workspace for <paramref name="user"/> (created + provisioned on first use), touched as active.</summary>
    Workspace Get(string user);

    /// <summary>Workspaces currently instantiated on this host (for background refresh / health).</summary>
    IReadOnlyCollection<Workspace> Active { get; }

    /// <summary>
    /// Disposes workspaces untouched for longer than <paramref name="maxIdle"/>, skipping any whose key
    /// <paramref name="isActive"/> reports as having a live circuit (so an open session is never evicted).
    /// </summary>
    void SweepIdle(TimeSpan maxIdle, Func<string, bool>? isActive = null);
}

/// <summary>
/// Lazily creates and caches one <see cref="Workspace"/> per user on this host, keyed by a sanitized
/// (filesystem- and writer-id-safe) user name. Each workspace's files live under
/// <c>&lt;usersRoot&gt;/&lt;key&gt;/local.db</c>; its writer id is <c>&lt;key&gt;@&lt;host&gt;</c> so concurrent hosts
/// write distinct per-writer remote logs. Thread-safe; disposes everything on host shutdown.
/// </summary>
public sealed class WorkspaceRegistry(string usersRoot, string host, WorkspaceDependencies deps) : IWorkspaceRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Workspace>> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    public Workspace Get(string user)
    {
        string key = Sanitize(user);
        Workspace workspace = _workspaces.GetOrAdd(key, k => new Lazy<Workspace>(() => Create(k))).Value;
        workspace.LastAccessUtc = DateTimeOffset.UtcNow;
        return workspace;
    }

    public IReadOnlyCollection<Workspace> Active =>
        _workspaces.Values.Where(l => l.IsValueCreated).Select(l => l.Value).ToList();

    public void SweepIdle(TimeSpan maxIdle, Func<string, bool>? isActive = null)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - maxIdle;
        foreach (KeyValuePair<string, Lazy<Workspace>> entry in _workspaces.ToArray())
        {
            if (isActive?.Invoke(entry.Key) == true)
            {
                continue; // a live circuit still holds this workspace's services — never evict it
            }

            if (entry.Value is { IsValueCreated: true, Value.LastAccessUtc: var last } && last < cutoff
                && _workspaces.TryRemove(entry.Key, out Lazy<Workspace>? removed) && removed.IsValueCreated)
            {
                removed.Value.Dispose(); // close the connection (WAL checkpoint); the db file stays as a cache
            }
        }
    }

    private Workspace Create(string key)
    {
        string dir = Path.Combine(usersRoot, key);
        Directory.CreateDirectory(dir);
        return new Workspace(Path.Combine(dir, "local.db"), $"{key}@{host}", deps);
    }

    /// <summary>Maps a user name to a key safe as a folder name and as a per-writer log filename.</summary>
    public static string Sanitize(string? user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return "unknown";
        }

        char[] chars = user.Trim().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_').ToArray();
        return new string(chars);
    }

    public void Dispose()
    {
        foreach (Lazy<Workspace> workspace in _workspaces.Values.Where(l => l.IsValueCreated))
        {
            workspace.Value.Dispose();
        }

        _workspaces.Clear();
    }
}
