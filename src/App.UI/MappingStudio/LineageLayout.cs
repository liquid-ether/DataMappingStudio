using System.Globalization;
using App.Application.Lineage;
using App.Domain.Lineage;

namespace App.UI.MappingStudio;

public sealed record LineageNodeBox(string Id, string Field, double RectX, double RectY, double RectW, double RectH, double CenterY, bool Highlight, bool Dim);

public sealed record LineageCardBox(string DataSet, bool IsTarget, double X, double Y, double W, double H, bool Dim, IReadOnlyList<LineageNodeBox> Nodes);

public sealed record LineageEdgeLine(string Path, bool Highlight, bool Dim);

public sealed record LineageLayoutModel(double Width, double Height, IReadOnlyList<LineageCardBox> Cards, IReadOnlyList<LineageEdgeLine> Edges);

/// <summary>
/// Computes SVG positions for the lineage graph — a faithful port of the mockup's <c>layoutLineage()</c>
/// (level columns by dataset depth, stacked cards, cubic-bezier edges, upstream-closure highlight + dim).
/// </summary>
public static class LineageLayout
{
    private const double ColW = 320, CardW = 210, HeadH = 30, RowH = 24, PadB = 8, Gap = 24, LeftPad = 26, TopPad = 24;

    public static LineageLayoutModel Compute(LineageScenario scenario, LineageGraph graph, LineageClosure? closure, string? selectedId)
    {
        bool hasSelection = selectedId is not null;
        HashSet<string> hlNodes = closure?.NodeIds.ToHashSet(StringComparer.Ordinal) ?? [];
        HashSet<int> hlEdges = closure?.EdgeIndices.ToHashSet() ?? [];

        // Group nodes by dataset, ordering fields canonically (provided-fields order, present only).
        var byDs = graph.Nodes
            .GroupBy(n => n.DataSet, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g =>
            {
                HashSet<string> present = g.Select(n => n.Field).ToHashSet(StringComparer.Ordinal);
                List<string> ordered = scenario.ProvidedFields(g.Key).Where(present.Contains).ToList();
                ordered.AddRange(present.Where(f => !ordered.Contains(f)));
                bool isTarget = g.First().Kind == LineageNodeKind.Target;
                return (IsTarget: isTarget, Fields: ordered);
            }, StringComparer.Ordinal);

        Dictionary<string, int> memo = [];
        var byLevel = byDs.Keys
            .GroupBy(ds => Level(scenario, ds, memo))
            .OrderBy(g => g.Key)
            .ToList();

        Dictionary<string, (double CardX, double CenterY)> pos = new(StringComparer.Ordinal);
        List<LineageCardBox> cards = [];
        double maxH = 0;
        int maxLv = 0;

        foreach (IGrouping<int, string> level in byLevel)
        {
            maxLv = Math.Max(maxLv, level.Key);
            double x = LeftPad + level.Key * ColW;
            double y = TopPad;

            foreach (string ds in level.OrderBy(d => d, StringComparer.Ordinal))
            {
                (bool isTarget, List<string> fields) = byDs[ds];
                double h = HeadH + fields.Count * RowH + PadB;
                List<LineageNodeBox> nodeBoxes = [];

                for (int k = 0; k < fields.Count; k++)
                {
                    string id = LineageNode.MakeId(ds, fields[k]);
                    double rectY = y + HeadH + k * RowH + 3;
                    double centerY = y + HeadH + k * RowH + RowH / 2;
                    pos[id] = (x, centerY);
                    bool hl = hlNodes.Contains(id);
                    nodeBoxes.Add(new LineageNodeBox(id, fields[k], x + 8, rectY, CardW - 16, RowH - 6, centerY, hl, hasSelection && !hl));
                }

                bool cardDim = hasSelection && !nodeBoxes.Any(n => n.Highlight);
                cards.Add(new LineageCardBox(ds, isTarget, x, y, CardW, h, cardDim, nodeBoxes));
                y += h + Gap;
            }

            maxH = Math.Max(maxH, y);
        }

        List<LineageEdgeLine> edges = [];
        for (int i = 0; i < graph.Edges.Count; i++)
        {
            LineageEdge e = graph.Edges[i];
            if (!pos.TryGetValue(e.FromId, out (double CardX, double CenterY) a) || !pos.TryGetValue(e.ToId, out (double CardX, double CenterY) b))
            {
                continue;
            }

            double x1 = a.CardX + CardW, y1 = a.CenterY, x2 = b.CardX, y2 = b.CenterY, cx = (x2 - x1) * 0.45;
            string path = string.Create(CultureInfo.InvariantCulture, $"M{x1},{y1} C{x1 + cx},{y1} {x2 - cx},{y2} {x2},{y2}");
            bool hl = hlEdges.Contains(i);
            edges.Add(new LineageEdgeLine(path, hl, hasSelection && !hl));
        }

        double width = LeftPad + maxLv * ColW + CardW + LeftPad;
        double height = Math.Max(maxH, 160);
        return new LineageLayoutModel(width, height, cards, edges);
    }

    private static int Level(LineageScenario scenario, string name, Dictionary<string, int> memo)
    {
        if (memo.TryGetValue(name, out int v))
        {
            return v;
        }

        LineageTarget? target = scenario.TargetByName(name);
        if (target is null)
        {
            return memo[name] = 0;
        }

        int max = 0;
        foreach (LineageSource s in target.Sources)
        {
            max = Math.Max(max, Level(scenario, s.Name, memo) + 1);
        }

        return memo[name] = max;
    }
}
