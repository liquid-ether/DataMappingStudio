namespace App.Application.Expressions;

/// <summary>
/// A reference available to expressions in the current scope. <see cref="Ref"/> is the token form
/// (<c>alias.field</c> or a bare self field), <see cref="Cls"/> the mockup chip class (a/b/g/tsrc/self),
/// and <see cref="EntryId"/> the linked dictionary entry when known (rename-safe lineage, §7b).
/// </summary>
public sealed record KnownReference(string Ref, string Cls, string Alias, string DataSet, Guid? EntryId = null);

/// <summary>Supplies the set of known references for the expression being edited/parsed.</summary>
public interface IResolutionContext
{
    IReadOnlyList<KnownReference> KnownReferences { get; }
}

/// <summary>A simple in-memory <see cref="IResolutionContext"/>.</summary>
public sealed class ResolutionContext(IReadOnlyList<KnownReference> knownReferences) : IResolutionContext
{
    public static readonly ResolutionContext Empty = new([]);

    public IReadOnlyList<KnownReference> KnownReferences { get; } = knownReferences;

    private Dictionary<string, KnownReference>? _byRef;

    public KnownReference? Resolve(string token)
    {
        _byRef ??= KnownReferences
            .GroupBy(r => r.Ref, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _byRef.GetValueOrDefault(token);
    }
}
