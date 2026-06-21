using App.Application.Expressions;
using App.Domain.Entities;
using App.Domain.Lineage;

namespace App.Application.Lineage;

/// <summary>A node in the "Built from" tree shown beside the lineage graph.</summary>
public sealed record LineageTreeNode(string DataSet, string Field, bool IsSourceTable, bool IsLeaf, IReadOnlyList<LineageTreeNode> Children);

/// <summary>The upstream highlight closure for a selected field: the nodes and edges on its lineage path.</summary>
public sealed record LineageClosure(IReadOnlySet<string> NodeIds, IReadOnlySet<int> EdgeIndices);

/// <summary>Builds field-level lineage by tracing expressions across a <see cref="LineageScenario"/>.</summary>
public interface ILineageEngine
{
    LineageGraph Build(LineageScenario scenario);

    LineageClosure UpstreamClosure(LineageGraph graph, string nodeId);

    LineageTreeNode? BuiltFrom(LineageScenario scenario, LineageGraph graph, string nodeId);
}

/// <summary>
/// Recursive field-derivation engine. Faithful port of the mockup's <c>buildLineage()</c> and the
/// "Built from" tree: tokenizes each field/calc expression, skips functions/keywords, follows
/// qualified <c>alias.field</c> references to their source (table column or another target's field) and
/// self references, producing nodes/edges and counting unresolved references. Cycle-guarded.
/// </summary>
public sealed class LineageEngine(FunctionLibrary functions) : ILineageEngine
{
    public LineageGraph Build(LineageScenario scenario)
    {
        Dictionary<string, LineageNode> nodes = new(StringComparer.Ordinal);
        List<LineageEdge> edges = [];
        int unresolved = 0;

        foreach (LineageTarget target in scenario.Targets)
        {
            foreach (string field in scenario.ProducedFields(target.Name))
            {
                string id = LineageNode.MakeId(target.Name, field);
                nodes[id] = new LineageNode(id, target.Name, field, LineageNodeKind.Target);
            }
        }

        foreach (LineageRow row in scenario.Rows)
        {
            if (row.Kind is not (MappingKind.Field or MappingKind.Calc))
            {
                continue;
            }

            LineageTarget? tg = scenario.TargetByName(row.Target);
            if (tg is null)
            {
                continue;
            }

            string toId = LineageNode.MakeId(row.Target, row.Field);

            foreach (string tk in ExpressionTokenizer.Tokenize(row.Expression).Where(ExpressionTokenizer.IsIdentifier))
            {
                if (functions.IsFunction(tk) || functions.IsKeyword(tk))
                {
                    continue;
                }

                int dot = tk.IndexOf('.', StringComparison.Ordinal);
                if (dot >= 0)
                {
                    string alias = tk[..dot];
                    string field = tk[(dot + 1)..];
                    LineageSource? s = tg.Sources.FirstOrDefault(s => s.Alias == alias);
                    if (s is null)
                    {
                        unresolved++;
                        continue;
                    }

                    if (scenario.ProvidedFields(s.Name).Contains(field))
                    {
                        string fromId = LineageNode.MakeId(s.Name, field);
                        if (!nodes.ContainsKey(fromId))
                        {
                            nodes[fromId] = new LineageNode(fromId, s.Name, field, s.IsTarget ? LineageNodeKind.Target : LineageNodeKind.SourceTable);
                        }

                        edges.Add(new LineageEdge(fromId, toId));
                    }
                    else
                    {
                        unresolved++;
                    }
                }
                else if (scenario.ProducedFields(row.Target).Contains(tk) && tk != row.Field)
                {
                    edges.Add(new LineageEdge(LineageNode.MakeId(row.Target, tk), toId));
                }
            }
        }

        return new LineageGraph(nodes.Values.ToList(), edges, unresolved);
    }

    public LineageClosure UpstreamClosure(LineageGraph graph, string nodeId)
    {
        Dictionary<string, List<int>> incoming = IncomingByNode(graph);
        HashSet<string> nodeIds = new(StringComparer.Ordinal) { nodeId };
        HashSet<int> edgeIndices = [];

        Stack<string> stack = new();
        stack.Push(nodeId);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!incoming.TryGetValue(current, out List<int>? edgeIdx))
            {
                continue;
            }

            foreach (int i in edgeIdx)
            {
                edgeIndices.Add(i);
                string from = graph.Edges[i].FromId;
                if (nodeIds.Add(from))
                {
                    stack.Push(from);
                }
            }
        }

        return new LineageClosure(nodeIds, edgeIndices);
    }

    public LineageTreeNode? BuiltFrom(LineageScenario scenario, LineageGraph graph, string nodeId)
    {
        Dictionary<string, LineageNode> byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (!byId.ContainsKey(nodeId))
        {
            return null;
        }

        Dictionary<string, List<string>> incoming = new(StringComparer.Ordinal);
        foreach (LineageEdge e in graph.Edges)
        {
            (incoming.TryGetValue(e.ToId, out List<string>? list) ? list : incoming[e.ToId] = []).Add(e.FromId);
        }

        return BuildTree(nodeId, byId, incoming, scenario, []);
    }

    private static LineageTreeNode BuildTree(
        string id,
        IReadOnlyDictionary<string, LineageNode> byId,
        IReadOnlyDictionary<string, List<string>> incoming,
        LineageScenario scenario,
        HashSet<string> seen)
    {
        LineageNode node = byId[id];
        bool isSourceTable = scenario.TargetByName(node.DataSet) is null;
        List<string> ups = incoming.TryGetValue(id, out List<string>? list)
            ? list.Where(u => !seen.Contains(u)).ToList()
            : [];
        bool isLeaf = ups.Count == 0;

        HashSet<string> next = new(seen, StringComparer.Ordinal) { id };
        List<LineageTreeNode> children = ups.Select(u => BuildTree(u, byId, incoming, scenario, next)).ToList();

        return new LineageTreeNode(node.DataSet, node.Field, isSourceTable, isLeaf, children);
    }

    private static Dictionary<string, List<int>> IncomingByNode(LineageGraph graph)
    {
        Dictionary<string, List<int>> incoming = new(StringComparer.Ordinal);
        for (int i = 0; i < graph.Edges.Count; i++)
        {
            string to = graph.Edges[i].ToId;
            (incoming.TryGetValue(to, out List<int>? list) ? list : incoming[to] = []).Add(i);
        }

        return incoming;
    }
}
