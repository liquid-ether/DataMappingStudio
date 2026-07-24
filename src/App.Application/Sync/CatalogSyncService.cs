using App.Application.Abstractions;
using App.Domain.Catalog;

namespace App.Application.Sync;

/// <summary>
/// Bridges the shared folder's meta-model logs and a working copy: <see cref="ApplyTo"/> folds every
/// writer's catalog changes and applies whatever this copy is missing (catalog rows, table metadata,
/// physical schema); <see cref="Publish"/> appends an admin's changes to their own log. The fold is
/// cached host-wide keyed by the catalog version signature, so on a multi-user host N workspaces fold
/// once per change, not once each. Thread-safe; all applies are idempotent.
/// </summary>
public sealed class CatalogSyncService(ICatalogRemote remote)
{
    private readonly object _gate = new();
    private string? _foldedVersion;
    private FoldedCatalog? _folded;

    /// <summary>Raised after a publish (hosts may use it to refresh navigation immediately).</summary>
    public event Action? Changed;

    /// <summary>Cheap current version signature (pass to <see cref="ApplyTo"/> callers for gating).</summary>
    public string Version() => remote.CatalogVersion();

    /// <summary>
    /// Applies the folded runtime meta-model to a working copy. Returns true when anything new was
    /// applied (callers then re-run <see cref="ILocalStore.EnsureSchema"/>-dependent caches/UI).
    /// </summary>
    public bool ApplyTo(ICatalog catalog, ITableCatalog tables, ILocalStore store)
    {
        FoldedCatalog folded = GetOrFold();
        if (folded.Tables.Count == 0 && folded.Columns.Count == 0)
        {
            return false;
        }

        bool applied = false;

        // Table metadata first (labels/nav/display), then columns, then the physical schema.
        Dictionary<string, TableCatalogEntry> currentMeta = tables.GetTableMeta().ToDictionary(t => t.TableName, StringComparer.Ordinal);
        foreach (TableCatalogEntry table in folded.Tables)
        {
            if (!currentMeta.TryGetValue(table.TableName, out TableCatalogEntry? existing) || existing != table)
            {
                tables.UpsertTableMeta(table);
                applied = true;
            }
        }

        Dictionary<(string, string), ColumnCatalogEntry> knownColumns = catalog.GetAll()
            .ToDictionary(c => (c.TableName, c.ColumnName));
        List<ColumnCatalogEntry> missing = [];
        foreach (ColumnCatalogEntry column in folded.Columns)
        {
            if (!knownColumns.TryGetValue((column.TableName, column.ColumnName), out ColumnCatalogEntry? existing))
            {
                missing.Add(column);
            }
            else if (existing.LabelEn != column.LabelEn || existing.LabelFr != column.LabelFr)
            {
                // Label edits are LWW presentation metadata; only labels are compared so drift in
                // structural defaults can never trigger rewrites.
                catalog.UpdateColumnMeta(column);
                applied = true;
            }
        }

        if (missing.Count > 0)
        {
            catalog.Seed(missing); // idempotent INSERT OR IGNORE
            applied = true;
        }

        if (applied)
        {
            store.EnsureSchema(); // idempotent CREATE TABLE / ADD COLUMN
        }

        return applied;
    }

    /// <summary>Appends meta-model changes to the writer's own log (sequence numbers assigned here).</summary>
    public void Publish(string writerId, IReadOnlyList<CatalogChangeEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        long seq = remote.ReadWriter(writerId).Select(e => e.ClientSeq).DefaultIfEmpty(0).Max();
        remote.Append(writerId, entries.Select(e => e with { ClientSeq = ++seq }).ToList());
        lock (_gate)
        {
            _foldedVersion = null; // our own write changed the remote; force a refold
        }

        NotifyChanged();
    }

    // Handlers run synchronously on the publisher's thread and may belong to OTHER circuits (a
    // disconnected-but-not-yet-disposed circuit's wizard, for instance) — a failing subscriber must
    // never fail the publish or crash the publishing circuit.
    private void NotifyChanged()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        foreach (Action handler in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception)
            {
                // Stale subscriber — ignored; it will catch up via the version-gated ApplyTo.
            }
        }
    }

    private FoldedCatalog GetOrFold()
    {
        string version = remote.CatalogVersion();
        lock (_gate)
        {
            if (version == _foldedVersion && _folded is not null)
            {
                return _folded;
            }
        }

        FoldedCatalog folded = CatalogFold.Fold(remote.ReadAll());
        lock (_gate)
        {
            _foldedVersion = version;
            _folded = folded;
        }

        return folded;
    }
}
