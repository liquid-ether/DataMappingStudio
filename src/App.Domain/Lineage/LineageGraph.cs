namespace App.Domain.Lineage;

/// <summary>Whether a lineage node is a produced target field or a raw source-table column.</summary>
public enum LineageNodeKind
{
    Target = 0,
    SourceTable = 1,
}

/// <summary>
/// A node in the field-level lineage graph: one field of one dataset. <see cref="Id"/> mirrors the
/// mockup's <c>dataset§field</c> key so the graph and the UI agree on identity.
/// </summary>
public sealed record LineageNode(string Id, string DataSet, string Field, LineageNodeKind Kind)
{
    public static string MakeId(string dataSet, string field) => $"{dataSet}§{field}";
}

/// <summary>A directed edge "from upstream field → to downstream field" derived from an expression.</summary>
public sealed record LineageEdge(string FromId, string ToId);

/// <summary>
/// The result of tracing field derivation: nodes, edges, and a count of references that could not be
/// resolved (excluded from the graph, surfaced as the mockup's "N unresolved references excluded").
/// </summary>
public sealed record LineageGraph(
    IReadOnlyList<LineageNode> Nodes,
    IReadOnlyList<LineageEdge> Edges,
    int UnresolvedCount)
{
    public static LineageGraph Empty { get; } = new([], [], 0);
}
