using App.Application.Expressions;
using App.Application.Lineage;
using App.Application.Mappings;
using App.Domain.Entities;

namespace App.UI.MappingStudio;

/// <summary>An editable mapping row in the studio grid; <see cref="Id"/> is its identity in the store.</summary>
public sealed class MappingRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required MappingKind Kind { get; set; }
    public required string Target { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public bool ProducesField => Kind is MappingKind.Field or MappingKind.Calc;

    public MappingRecord ToRecord() => new(Id, Target, Field, Kind, Type, Expression);

    public static MappingRow FromRecord(MappingRecord r) => new()
    {
        Id = r.Id,
        Kind = r.Kind,
        Target = r.Target,
        Field = r.Field,
        Type = r.Type,
        Expression = r.Expression,
    };
}

/// <summary>
/// The Mapping Studio model, backed by the local store via <see cref="IMappingRepository"/>: rows are
/// loaded from the <c>mapping</c> table and every add/edit/delete is persisted, so changes flow into
/// the change log and publish/sync (Architecture §6/§8). The target/source (alias) structure used for
/// lineage context is seeded configuration for now. On first run (empty table) the demo is seeded.
/// </summary>
public sealed class MappingStudioState(IMappingRepository repository)
{
    private const string WriterId = "analyst";

    private static readonly Dictionary<MappingKind, int> KindOrder = new()
    {
        [MappingKind.Join] = 0,
        [MappingKind.Filter] = 1,
        [MappingKind.Field] = 2,
        [MappingKind.Calc] = 3,
    };

    private readonly List<MappingRow> _rows = [];
    private bool _loaded;

    public List<LineageTarget> Targets { get; } =
    [
        new("CUSTOMER_360",
        [
            LineageSource.Table("a", "a", "CRM_ACCOUNTS", ["acct_id", "first_name", "last_name", "email", "status", "region", "created_at"]),
            LineageSource.Table("b", "b", "BILLING", ["account_ref", "mrr", "plan", "balance", "last_payment"]),
        ]),
        new("MARKETING_SEGMENTS", [LineageSource.Target("c", "tsrc", "CUSTOMER_360")]),
    ];

    public IReadOnlyList<MappingRow> Rows
    {
        get
        {
            EnsureLoaded();
            return _rows;
        }
    }

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
        EnsureLoaded();
        MappingRow row = kind switch
        {
            MappingKind.Join => new MappingRow { Kind = kind, Target = target, Expression = "a.field = b.field" },
            MappingKind.Filter => new MappingRow { Kind = kind, Target = target, Expression = "a.field = 'value'" },
            MappingKind.Calc => new MappingRow { Kind = kind, Target = target, Field = "new_indicator", Expression = "IF(lifetime_value > 0, 1, 0)", Type = "bool" },
            _ => new MappingRow { Kind = MappingKind.Field, Target = target, Field = "new_field", Expression = "a.field", Type = "text" },
        };
        _rows.Add(row);
        repository.Save(row.ToRecord(), WriterId);
    }

    /// <summary>Persists an in-place edit of a row (call after changing kind/field/type/target/expression).</summary>
    public void Save(MappingRow row) => repository.Save(row.ToRecord(), WriterId);

    public void DeleteRow(MappingRow row)
    {
        EnsureLoaded();
        _rows.Remove(row);
        repository.Delete(row.Id, WriterId);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        List<MappingRecord> existing = repository.GetAll().ToList();
        if (existing.Count == 0)
        {
            foreach (MappingRow seed in DemoRows())
            {
                _rows.Add(seed);
                repository.Save(seed.ToRecord(), WriterId);
            }

            return;
        }

        _rows.AddRange(existing.Select(MappingRow.FromRecord));
    }

    private static IEnumerable<MappingRow> DemoRows() =>
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
}
