using App.Domain.Data;
using App.Domain.Entities;

namespace App.Application.Provisioning;

/// <summary>
/// A generated set of catalog-shaped sample rows per table (canonical string values), ready to be
/// written to the local store as a baseline dataset.
/// </summary>
public sealed record SampleDataSet(
    IReadOnlyList<Row> Applications,
    IReadOnlyList<Row> Classifications,
    IReadOnlyList<Row> LookupValues,
    IReadOnlyList<Row> DataSources,
    IReadOnlyList<Row> DictionaryEntries,
    IReadOnlyList<Row> Rules,
    IReadOnlyList<Row> MappingTargets,
    IReadOnlyList<Row> MappingSources,
    IReadOnlyList<Row> Mappings)
{
    /// <summary>All tables in FK/dependency order so referenced rows are written before referencing rows.</summary>
    public IReadOnlyList<(string Table, IReadOnlyList<Row> Rows)> AllTables() =>
    [
        (TableNames.Application, Applications),
        (TableNames.Classification, Classifications),
        (TableNames.LookupValue, LookupValues),
        (TableNames.DataSource, DataSources),
        (TableNames.DictionaryEntry, DictionaryEntries),
        (TableNames.Rule, Rules),
        (TableNames.MappingTarget, MappingTargets),
        (TableNames.MappingSource, MappingSources),
        (TableNames.Mapping, Mappings),
    ];

    public int TotalRows => AllTables().Sum(t => t.Rows.Count);
}

/// <summary>
/// Deterministic generator for a realistic demo dataset spanning every entity: 10 applications,
/// 100 data sources, 5000 dictionary entries (5–150 fields per source), a handful of classifications,
/// lookups and rules, and a 30-target Mapping Studio model whose lineage chains are at most 5 targets
/// deep. Pure — produces in-memory <see cref="Row"/>s with canonical values; a seeder writes them.
/// </summary>
public static class SampleData
{
    public const int ApplicationCount = 10;
    public const int DataSourceCount = 100;
    public const int DictionaryEntryCount = 5000;
    public const int MinFieldsPerSource = 5;
    public const int MaxFieldsPerSource = 150;

    /// <summary>Target counts per lineage level (sums to 30); deeper levels build on shallower ones.</summary>
    public static readonly IReadOnlyList<int> TargetsPerLevel = [10, 8, 6, 4, 2];

    public const int MaxLineageDepth = 5;

    // The fields every Mapping Studio target produces; downstream targets reference these by alias.
    private static readonly string[] ProducedFields = ["id", "label", "amount", "flag"];

    // The first columns every data source is guaranteed to have (min 5 fields per source), so level-1
    // mapping expressions can reference real dictionary column names.
    private static readonly string[] RawSourceFields = ["col_001", "col_002", "col_003", "col_004", "col_005"];

    private static readonly string[] SourceTypes = ["TABLE", "FILE", "VIEW", "API", "STREAM"];
    private static readonly string[] Frequencies = ["DAILY", "WEEKLY", "MONTHLY", "HOURLY", "ADHOC"];
    private static readonly string[] Blocs = ["FINANCE", "RISK", "MARKETING", "OPERATIONS", "HR"];
    private static readonly string[] Statuses = ["ACTIVE", "DRAFT", "DEPRECATED"];
    private static readonly string[] DataTypes = ["VARCHAR(255)", "INT", "DECIMAL(18,2)", "DATE", "TIMESTAMP", "BOOLEAN", "TEXT"];
    private static readonly string[] Languages = ["EN", "FR"];

    private static readonly (string Prp, string Access, string Disclosure)[] Classes =
    [
        ("PUBLIC", "901-0", "902-0"),
        ("INTERNAL", "901-1", "902-1"),
        ("CONFIDENTIAL", "901-2", "902-2"),
        ("RESTRICTED", "901-3", "902-3"),
        ("SECRET", "901-4", "902-4"),
    ];

    public static SampleDataSet Build(int seed = 20260623)
    {
        Random rnd = new(seed);

        // --- Applications ---
        List<Row> applications = [];
        for (int i = 1; i <= ApplicationCount; i++)
        {
            string code = $"APP{i:D2}";
            applications.Add(Make(TableNames.Application,
                ("app_code", code),
                ("name_gdm", $"{code} GDM system"),
                ("name_va360", $"{code} VA360"),
                ("short_name", code),
                ("gold_root_path", $"/gold/{code.ToLowerInvariant()}"),
                ("description", $"Sample application {i}"),
                ("responsible_it", $"it.owner{i}@example.com"),
                ("responsible_business", $"biz.owner{i}@example.com")));
        }

        // --- Classifications ---
        List<Row> classifications = Classes.Select(c => Make(TableNames.Classification,
            ("prp", c.Prp),
            ("access_901", c.Access),
            ("disclosure_902", c.Disclosure))).ToList();

        // --- Lookup values (several lookup_type groups) ---
        List<Row> lookups = [];
        AddLookups(lookups, "status", Statuses);
        AddLookups(lookups, "frequency", Frequencies);
        AddLookups(lookups, "source_type", SourceTypes);
        AddLookups(lookups, "bloc", Blocs);

        // --- Data sources (each linked to an application) ---
        List<Row> dataSources = [];
        for (int i = 1; i <= DataSourceCount; i++)
        {
            string name = $"SRC_{i:D3}";
            Row app = applications[(i - 1) % applications.Count];
            dataSources.Add(Make(TableNames.DataSource,
                ("application_id", app.Id.ToString()),
                ("name", name),
                ("description", $"Sample data source {i}"),
                ("type", Pick(rnd, SourceTypes)),
                ("bloc", Pick(rnd, Blocs)),
                ("frequency", Pick(rnd, Frequencies)),
                ("full_name", $"{Pick(rnd, Blocs)}.{name}"),
                ("bronze_path", $"/bronze/{name.ToLowerInvariant()}"),
                ("silver_path", $"/silver/{name.ToLowerInvariant()}"),
                ("gold_path", $"/gold/{name.ToLowerInvariant()}"),
                ("status", Pick(rnd, Statuses))));
        }

        // --- Dictionary entries: distribute 5000 across the 100 sources, 5–150 each ---
        int[] fieldCounts = DistributeFields(rnd);
        List<Row> dictionary = [];
        for (int s = 0; s < dataSources.Count; s++)
        {
            Row source = dataSources[s];
            for (int k = 1; k <= fieldCounts[s]; k++)
            {
                Row cls = classifications[rnd.Next(classifications.Count)];
                bool isPk = k == 1;
                dictionary.Add(Make(TableNames.DictionaryEntry,
                    ("source_id", source.Id.ToString()),
                    ("column_name", $"col_{k:D3}"),
                    ("ordinal", k.ToString()),
                    ("data_type", isPk ? "INT" : Pick(rnd, DataTypes)),
                    ("is_primary_key", isPk ? "true" : "false"),
                    ("is_nullable", isPk ? "false" : "true"),
                    ("business_name", $"{source["name"]} field {k}"),
                    ("description", $"Sample column {k} of {source["name"]}"),
                    ("classification_id", cls.Id.ToString()),
                    ("language", Pick(rnd, Languages)),
                    ("status", "ACTIVE")));
            }
        }

        // --- Reusable rules ---
        List<Row> rules =
        [
            Rule("TRIM_UPPER", "Normalize a code", "UPPER(TRIM(a.code))"),
            Rule("FULL_NAME", "Concatenate names", "CONCAT(a.first_name,' ',a.last_name)"),
            Rule("SAFE_AMOUNT", "Default missing amounts", "COALESCE(a.amount, 0)"),
            Rule("RISK_BAND", "Bucket a value", "CASE WHEN amount >= 5000 THEN 'HIGH' WHEN amount >= 1000 THEN 'MED' ELSE 'LOW' END"),
            Rule("IS_ACTIVE", "Active flag", "IF(a.status = 'ACTIVE', 1, 0)"),
        ];

        // --- Mapping Studio model: 30 targets across 5 lineage levels ---
        (List<Row> mappingTargets, List<Row> mappingSources, List<Row> mappings) = BuildMappingModel(dataSources);

        return new SampleDataSet(applications, classifications, lookups, dataSources, dictionary, rules, mappingTargets, mappingSources, mappings);
    }

    private static (List<Row> Targets, List<Row> Sources, List<Row> Rows) BuildMappingModel(List<Row> dataSources)
    {
        List<Row> targets = [];
        List<Row> sources = [];
        List<Row> rows = [];
        List<List<string>> byLevel = [];

        for (int level = 1; level <= TargetsPerLevel.Count; level++)
        {
            List<string> names = [];
            int size = TargetsPerLevel[level - 1];

            for (int idx = 0; idx < size; idx++)
            {
                string name = $"TGT_L{level}_{idx:D2}";
                names.Add(name);
                targets.Add(Make(TableNames.MappingTarget, ("name", name)));

                if (level == 1)
                {
                    // Built from two raw data sources (real names + their guaranteed columns).
                    string a = dataSources[(2 * idx) % dataSources.Count]["name"]!;
                    string b = dataSources[(2 * idx + 1) % dataSources.Count]["name"]!;
                    AddSource(sources, name, "a", "a", a, isTarget: false, RawSourceFields);
                    AddSource(sources, name, "b", "b", b, isTarget: false, RawSourceFields);

                    AddMapping(rows, name, "Join", null, null, "a.col_001 = b.col_001");
                    AddMapping(rows, name, "Filter", null, null, "a.col_005 IS NOT NULL");
                    AddMapping(rows, name, "Field", "id", "text", "a.col_001");
                    AddMapping(rows, name, "Field", "label", "text", "CONCAT(a.col_002,' ',a.col_003)");
                    AddMapping(rows, name, "Field", "amount", "number", "COALESCE(b.col_004, 0)");
                    AddMapping(rows, name, "Calc", "flag", "bool", "IF(amount > 0, 1, 0)");
                }
                else
                {
                    // Built from two targets one level shallower (so the deepest chain is exactly 5 deep).
                    List<string> prev = byLevel[level - 2];
                    string u = prev[idx % prev.Count];
                    string v = prev[(idx + 1) % prev.Count];
                    AddSource(sources, name, "u", "tsrc", u, isTarget: true, []);
                    AddSource(sources, name, "v", "tsrc", v, isTarget: true, []);

                    AddMapping(rows, name, "Join", null, null, "u.id = v.id");
                    AddMapping(rows, name, "Field", "id", "text", "u.id");
                    AddMapping(rows, name, "Field", "label", "text", "COALESCE(u.label, v.label)");
                    AddMapping(rows, name, "Field", "amount", "number", "u.amount * 2");
                    AddMapping(rows, name, "Calc", "flag", "bool", "IF(u.flag = 1, 1, 0)");
                }
            }

            byLevel.Add(names);
        }

        return (targets, sources, rows);
    }

    /// <summary>Gives each source 5 fields, then randomly distributes the remainder up to 150 each.</summary>
    private static int[] DistributeFields(Random rnd)
    {
        int[] counts = new int[DataSourceCount];
        Array.Fill(counts, MinFieldsPerSource);
        int remaining = DictionaryEntryCount - (DataSourceCount * MinFieldsPerSource);

        while (remaining > 0)
        {
            int i = rnd.Next(DataSourceCount);
            if (counts[i] < MaxFieldsPerSource)
            {
                counts[i]++;
                remaining--;
            }
        }

        return counts;
    }

    private static void AddLookups(List<Row> into, string type, string[] values)
    {
        foreach (string value in values)
        {
            into.Add(Make(TableNames.LookupValue,
                ("lookup_type", type),
                ("value", value),
                ("description_en", $"{type}: {value}"),
                ("description_fr", $"{type} : {value}")));
        }
    }

    private static Row Rule(string name, string description, string expression) =>
        Make(TableNames.Rule, ("name", name), ("description", description), ("expression", expression));

    private static void AddSource(List<Row> into, string target, string alias, string cls, string name, bool isTarget, IReadOnlyList<string> fields) =>
        into.Add(Make(TableNames.MappingSource,
            ("target", target),
            ("alias", alias),
            ("cls", cls),
            ("source_name", name),
            ("is_target", isTarget ? "true" : "false"),
            ("fields", string.Join(",", fields))));

    private static void AddMapping(List<Row> into, string target, string kind, string? field, string? type, string expression) =>
        into.Add(Make(TableNames.Mapping,
            ("target", target),
            ("field", field),
            ("kind", kind),
            ("type", type),
            ("expression", expression),
            ("is_tokenized", "false")));

    private static Row Make(string table, params (string Column, string? Value)[] values)
    {
        Row row = new(table, Guid.NewGuid());
        foreach ((string column, string? value) in values)
        {
            if (value is not null)
            {
                row[column] = value;
            }
        }

        return row;
    }

    private static string Pick(Random rnd, string[] options) => options[rnd.Next(options.Length)];
}
