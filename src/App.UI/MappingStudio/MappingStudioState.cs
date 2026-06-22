using App.Application.Expressions;
using App.Application.Lineage;
using App.Domain.Entities;

namespace App.UI.MappingStudio;

/// <summary>An editable mapping row in the studio grid (mirrors the mockup's <c>state.rows</c> entries).</summary>
public sealed class MappingRow
{
    public required MappingKind Kind { get; set; }
    public required string Target { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public bool ProducesField => Kind is MappingKind.Field or MappingKind.Calc;
}

/// <summary>
/// In-memory editable Mapping Studio model, scoped per circuit and shared by the Mappings and Lineage
/// views. A faithful port of the mockup's client-side <c>state</c>; it builds a <see cref="LineageScenario"/>
/// on demand so the expression/lineage engines drive highlighting and tracing. Persisting these rows to
/// the <c>mapping</c> table is a later refinement (the studio currently edits an in-memory model, as the
/// mockup did).
/// </summary>
public sealed class MappingStudioState
{
    private static readonly Dictionary<MappingKind, int> KindOrder = new()
    {
        [MappingKind.Join] = 0,
        [MappingKind.Filter] = 1,
        [MappingKind.Field] = 2,
        [MappingKind.Calc] = 3,
    };

    public List<LineageTarget> Targets { get; } =
    [
        new("CUSTOMER_360",
        [
            LineageSource.Table("a", "a", "CRM_ACCOUNTS", ["acct_id", "first_name", "last_name", "email", "status", "region", "created_at"]),
            LineageSource.Table("b", "b", "BILLING", ["account_ref", "mrr", "plan", "balance", "last_payment"]),
        ]),
        new("MARKETING_SEGMENTS", [LineageSource.Target("c", "tsrc", "CUSTOMER_360")]),
    ];

    public List<MappingRow> Rows { get; } =
    [
        new() { Kind = MappingKind.Join, Target = "CUSTOMER_360", Expression = "a.acct_id = b.account_ref" },
        new() { Kind = MappingKind.Filter, Target = "CUSTOMER_360", Expression = "a.status IN ('ACTIVE','PENDING')" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "customer_id", Expression = "a.acct_id", Type = "text" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "full_name", Expression = "CONCAT(a.first_name,' ',a.last_name)", Type = "text" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "email", Expression = "a.email", Type = "text" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "region", Expression = "a.region", Type = "text" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "lifetime_value", Expression = "COALESCE(b.mrr,0) * 12", Type = "number" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "plan", Expression = "b.plan", Type = "text" },
        new() { Kind = MappingKind.Field, Target = "CUSTOMER_360", Field = "plan_label", Expression = "b.plan_label", Type = "text" },
        new() { Kind = MappingKind.Calc, Target = "CUSTOMER_360", Field = "risk_band", Expression = "CASE WHEN lifetime_value >= 5000 THEN 'HIGH' WHEN lifetime_value >= 1000 THEN 'MED' ELSE 'LOW' END", Type = "text" },
        new() { Kind = MappingKind.Calc, Target = "CUSTOMER_360", Field = "is_high_value", Expression = "IF(lifetime_value >= 1200, 1, 0)", Type = "bool" },
        new() { Kind = MappingKind.Filter, Target = "MARKETING_SEGMENTS", Expression = "c.is_high_value = 1" },
        new() { Kind = MappingKind.Field, Target = "MARKETING_SEGMENTS", Field = "base_risk", Expression = "c.risk_band", Type = "text" },
        new() { Kind = MappingKind.Field, Target = "MARKETING_SEGMENTS", Field = "segment_code", Expression = "UPPER(c.plan)", Type = "text" },
        new() { Kind = MappingKind.Calc, Target = "MARKETING_SEGMENTS", Field = "priority", Expression = "IF(c.risk_band = 'HIGH' AND c.is_high_value = 1, 'P1', 'P2')", Type = "text" },
    ];

    public LineageScenario BuildScenario()
        => new(Targets, Rows.Select(r => new LineageRow(r.Kind, r.Target, r.Field, r.Expression)).ToList());

    public IReadOnlyList<KnownReference> KnownReferences(string target) => BuildScenario().KnownReferences(target);

    public IReadOnlyList<string> TargetNames => Targets.Select(t => t.Name).ToList();

    /// <summary>Rows filtered by target/search and ordered the mockup way (by target, then kind, then index).</summary>
    public IReadOnlyList<(MappingRow Row, int Index)> VisibleRows(string? search, string? targetFilter)
    {
        IEnumerable<(MappingRow Row, int Index)> rows = Rows.Select((r, i) => (r, i));

        if (!string.IsNullOrEmpty(targetFilter) && targetFilter != "*")
        {
            rows = rows.Where(x => x.Row.Target == targetFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLowerInvariant();
            rows = rows.Where(x => $"{x.Row.Target} {x.Row.Field} {x.Row.Expression}".ToLowerInvariant().Contains(q));
        }

        return rows
            .OrderBy(x => x.Row.Target, StringComparer.Ordinal)
            .ThenBy(x => KindOrder[x.Row.Kind])
            .ThenBy(x => x.Index)
            .ToList();
    }

    public void AddRow(MappingKind kind, string target)
    {
        MappingRow row = kind switch
        {
            MappingKind.Join => new MappingRow { Kind = kind, Target = target, Expression = "a.field = b.field" },
            MappingKind.Filter => new MappingRow { Kind = kind, Target = target, Expression = "a.field = 'value'" },
            MappingKind.Calc => new MappingRow { Kind = kind, Target = target, Field = "new_indicator", Expression = "IF(lifetime_value > 0, 1, 0)", Type = "bool" },
            _ => new MappingRow { Kind = MappingKind.Field, Target = target, Field = "new_field", Expression = "a.field", Type = "text" },
        };
        Rows.Add(row);
    }

    public void DeleteRow(MappingRow row) => Rows.Remove(row);
}
