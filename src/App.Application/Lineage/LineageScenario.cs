using App.Application.Expressions;
using App.Domain.Entities;

namespace App.Application.Lineage;

/// <summary>A source feeding a target: a raw table (with its own <see cref="Fields"/>) or another target.</summary>
public sealed record LineageSource(string Alias, string Cls, string Name, bool IsTarget, IReadOnlyList<string> Fields)
{
    public static LineageSource Table(string alias, string cls, string name, IReadOnlyList<string> fields)
        => new(alias, cls, name, false, fields);

    public static LineageSource Target(string alias, string cls, string name)
        => new(alias, cls, name, true, []);
}

/// <summary>A mapping target and the aliased sources its expressions may reference.</summary>
public sealed record LineageTarget(string Name, IReadOnlyList<LineageSource> Sources);

/// <summary>A single mapping row (kind/target/field/expression), mirroring the mockup grid.</summary>
public sealed record LineageRow(MappingKind Kind, string Target, string Field, string Expression);

/// <summary>
/// The whole mapping model the lineage engine traces over — a domain-agnostic mirror of the mockup's
/// <c>state</c>. Phase 7 adapts domain mappings/sources/dictionary entries into this; the engine and its
/// tests work against it identically.
/// </summary>
public sealed class LineageScenario(IReadOnlyList<LineageTarget> targets, IReadOnlyList<LineageRow> rows)
{
    public IReadOnlyList<LineageTarget> Targets { get; } = targets;

    public IReadOnlyList<LineageRow> Rows { get; } = rows;

    public LineageTarget? TargetByName(string name) => Targets.FirstOrDefault(t => t.Name == name);

    /// <summary>Fields a target produces (its field/calc rows), in row order, de-duplicated.</summary>
    public IReadOnlyList<string> ProducedFields(string name)
    {
        List<string> fields = [];
        foreach (LineageRow r in Rows)
        {
            if (r.Target == name && (r.Kind is MappingKind.Field or MappingKind.Calc) && !fields.Contains(r.Field))
            {
                fields.Add(r.Field);
            }
        }

        return fields;
    }

    /// <summary>Fields a dataset can provide: produced fields if it's a target, else a source table's columns.</summary>
    public IReadOnlyList<string> ProvidedFields(string name)
    {
        if (TargetByName(name) is not null)
        {
            return ProducedFields(name);
        }

        foreach (LineageTarget t in Targets)
        {
            LineageSource? s = t.Sources.FirstOrDefault(s => s.Name == name && !s.IsTarget);
            if (s is not null)
            {
                return s.Fields;
            }
        }

        return [];
    }

    /// <summary>The references valid inside <paramref name="targetName"/>'s expressions (for the classifier).</summary>
    public IReadOnlyList<KnownReference> KnownReferences(string targetName)
    {
        List<KnownReference> refs = [];
        LineageTarget? tg = TargetByName(targetName);
        if (tg is null)
        {
            return refs;
        }

        foreach (LineageSource s in tg.Sources)
        {
            IReadOnlyList<string> fields = s.IsTarget ? ProducedFields(s.Name) : s.Fields;
            foreach (string f in fields)
            {
                refs.Add(new KnownReference($"{s.Alias}.{f}", s.Cls, s.Alias, s.Name));
            }
        }

        foreach (string f in ProducedFields(targetName))
        {
            refs.Add(new KnownReference(f, "self", "·", targetName));
        }

        return refs;
    }
}
