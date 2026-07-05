using App.Domain.Catalog;

namespace App.Application.Abstractions;

/// <summary>
/// The shared folder's meta-model change logs — per-writer append-only files under
/// <c>_meta/catalog/</c>, mirroring the data-change design (only the owning writer ever writes their
/// file, so concurrent admins cannot collide). The deterministic fold of every writer's entries is the
/// team's runtime meta-model, applied on top of the built-in defaults.
/// </summary>
public interface ICatalogRemote
{
    /// <summary>Cheap signature of the catalog logs (names+sizes+mtimes) to skip no-op folds.</summary>
    string CatalogVersion();

    IReadOnlyList<CatalogChangeEntry> ReadAll();

    /// <summary>Reads one writer's entries (for sequence continuation).</summary>
    IReadOnlyList<CatalogChangeEntry> ReadWriter(string writerId);

    void Append(string writerId, IReadOnlyList<CatalogChangeEntry> entries);
}
