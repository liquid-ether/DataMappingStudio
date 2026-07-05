using App.Domain.Catalog;
using App.Domain.Entities;

namespace App.Application.Provisioning;

/// <summary>An entity surfaced in the UI navigation: its table and the i18n nav key for its label.</summary>
public sealed record EntityNav(string Table, string NavKey);

/// <summary>
/// The default column catalog for the normalized entities (Architecture §4, Appendix A). Seeds the
/// principal columns the editors expose. Reference/lookup columns (status, type, FKs, classification…)
/// are seeded as scalar text here — usable for entry now; Phase 2 (v1.0.0) upgrades them to reference
/// columns with pickers + autofill, needing no migration since the catalog already supports it.
/// The full per-field catalog is reconciled from <c>TableDefinition.xlsx</c> by the importer (v0.10.0).
/// </summary>
public static class DefaultCatalog
{
    public static IReadOnlyList<EntityNav> Navigation { get; } =
    [
        new(TableNames.Application, "navApplications"),
        new(TableNames.DataSource, "navSources"),
        new(TableNames.DictionaryEntry, "navDictionary"),
        new(TableNames.LookupValue, "navConfig"),
        new(TableNames.Classification, "navClassification"),
        new(TableNames.Rule, "navRules"),
    ];

    /// <summary>
    /// Default table-level metadata: navigation placement mirrors <see cref="Navigation"/> (whose i18n
    /// keys stay authoritative for the default tables' labels — the literals here are fallbacks), and
    /// the reference-picker display column per table. Seeded idempotently; runtime tables add their own.
    /// </summary>
    public static IReadOnlyList<TableCatalogEntry> TableMeta() =>
    [
        new() { TableName = TableNames.Application, LabelEn = "Applications", LabelFr = "Applications", NavVisible = true, NavOrder = 1, DisplayColumn = "app_code" },
        new() { TableName = TableNames.DataSource, LabelEn = "Sources", LabelFr = "Sources", NavVisible = true, NavOrder = 2, DisplayColumn = "name" },
        new() { TableName = TableNames.DictionaryEntry, LabelEn = "Dictionary", LabelFr = "Dictionnaire", NavVisible = true, NavOrder = 3, DisplayColumn = "column_name" },
        new() { TableName = TableNames.LookupValue, LabelEn = "Config", LabelFr = "Config", NavVisible = true, NavOrder = 4, DisplayColumn = "value" },
        new() { TableName = TableNames.Classification, LabelEn = "Classification", LabelFr = "Classification", NavVisible = true, NavOrder = 5, DisplayColumn = "prp" },
        new() { TableName = TableNames.Rule, LabelEn = "Rules", LabelFr = "Règles", NavVisible = true, NavOrder = 6, DisplayColumn = "name" },
        new() { TableName = TableNames.Mapping, LabelEn = "Mappings", LabelFr = "Mappages", NavVisible = false, NavOrder = 100 },
        new() { TableName = TableNames.MappingTarget, LabelEn = "Mapping targets", LabelFr = "Cibles de mappage", NavVisible = false, NavOrder = 101 },
        new() { TableName = TableNames.MappingSource, LabelEn = "Mapping sources", LabelFr = "Sources de mappage", NavVisible = false, NavOrder = 102 },
    ];

    public static IReadOnlyList<ColumnCatalogEntry> Entries()
    {
        Builder b = new();

        b.Table(TableNames.Application)
            .Col("app_code", CatalogValueType.Text, "App code", "Code applicatif", required: true)
            .Col("name_gdm", CatalogValueType.Text, "GDM name", "Nom GDM")
            .Col("name_va360", CatalogValueType.Text, "VA360 name", "Nom VA360")
            .Col("short_name", CatalogValueType.Text, "Short name", "Nom abrégé")
            .Col("gold_root_path", CatalogValueType.Text, "Gold path", "Répertoire Valo (Gold)")
            .Col("description", CatalogValueType.Text, "Description", "Description")
            .Col("responsible_it", CatalogValueType.Text, "Responsible IT", "Responsable TI")
            .Col("responsible_business", CatalogValueType.Text, "Responsible business", "Responsable affaire");

        b.Table(TableNames.DataSource)
            .Ref("application_id", TableNames.Application, "Application", "Application")
            .Col("name", CatalogValueType.Text, "Name", "Nom", required: true)
            .Col("description", CatalogValueType.Text, "Description", "Description")
            .Col("type", CatalogValueType.Text, "Type", "Type")
            .Col("bloc", CatalogValueType.Text, "Bloc", "Bloc")
            .Col("frequency", CatalogValueType.Text, "Frequency", "Fréquence")
            .Col("full_name", CatalogValueType.Text, "Full name", "Nom complet")
            .Col("bronze_path", CatalogValueType.Text, "Bronze path", "Répertoire brute")
            .Col("silver_path", CatalogValueType.Text, "Silver path", "Répertoire standardisé")
            .Col("gold_path", CatalogValueType.Text, "Gold path", "Répertoire Valo (Gold)")
            .Col("status", CatalogValueType.Text, "Status", "Statut")
            .Computed("field_count", "count(dictionary_entry.source_id)", "NB fields", "NB champs");

        b.Table(TableNames.DictionaryEntry)
            .Ref("source_id", TableNames.DataSource, "Source", "Source")
            .Col("column_name", CatalogValueType.Text, "Column", "Colonne", required: true)
            .Col("ordinal", CatalogValueType.Integer, "Order", "Ordre")
            .Col("data_type", CatalogValueType.Text, "Data type", "Type de données")
            .Col("is_primary_key", CatalogValueType.Boolean, "Primary key", "Clé primaire")
            .Col("is_nullable", CatalogValueType.Boolean, "Nullable", "Nullable")
            .Col("business_name", CatalogValueType.Text, "Business name", "Nom affaire")
            .Col("description", CatalogValueType.Text, "Description", "Description")
            .Ref("classification_id", TableNames.Classification, "PRP", "PRP")
            .Computed("access_901", "lookup(classification_id.access_901)", "Access 901", "Accès 901")
            .Computed("disclosure_902", "lookup(classification_id.disclosure_902)", "Disclosure 902", "Divulgation 902")
            .Col("language", CatalogValueType.Text, "Language", "Langue")
            .Col("status", CatalogValueType.Text, "Status", "Statut");

        b.Table(TableNames.LookupValue)
            .Col("lookup_type", CatalogValueType.Text, "Type", "Type", required: true)
            .Col("value", CatalogValueType.Text, "Value", "Valeur", required: true)
            .Col("description_en", CatalogValueType.Text, "Description (EN)", "Description (EN)")
            .Col("description_fr", CatalogValueType.Text, "Description (FR)", "Description (FR)");

        b.Table(TableNames.Classification)
            .Col("prp", CatalogValueType.Text, "PRP", "PRP", required: true)
            .Col("access_901", CatalogValueType.Text, "Access 901", "Accès 901")
            .Col("disclosure_902", CatalogValueType.Text, "Disclosure 902", "Divulgation 902");

        b.Table(TableNames.Rule)
            .Col("name", CatalogValueType.Text, "Name", "Nom", required: true)
            .Col("description", CatalogValueType.Text, "Description", "Description")
            .Col("expression", CatalogValueType.Text, "Expression", "Expression");

        b.Table(TableNames.Mapping)
            .Col("target", CatalogValueType.Text, "Target", "Cible")
            .Col("field", CatalogValueType.Text, "Field", "Champ")
            .Col("kind", CatalogValueType.Text, "Kind", "Type")
            .Col("type", CatalogValueType.Text, "Data type", "Type de données")
            .Col("expression", CatalogValueType.Text, "Expression", "Expression")
            .Col("is_tokenized", CatalogValueType.Boolean, "Tokenized", "Tokenisé")
            .Col("notes", CatalogValueType.Text, "Notes", "Notes");

        // Mapping Studio target/source (alias) structure — persisted so the lineage context syncs too.
        b.Table(TableNames.MappingTarget)
            .Col("name", CatalogValueType.Text, "Name", "Nom", required: true);

        b.Table(TableNames.MappingSource)
            .Col("target", CatalogValueType.Text, "Target", "Cible", required: true)
            .Col("alias", CatalogValueType.Text, "Alias", "Alias", required: true)
            .Col("cls", CatalogValueType.Text, "Class", "Classe")
            .Col("source_name", CatalogValueType.Text, "Source", "Source", required: true)
            .Col("is_target", CatalogValueType.Boolean, "Is target", "Est une cible")
            .Col("fields", CatalogValueType.Text, "Fields", "Champs");

        return b.Build();
    }

    private sealed class Builder
    {
        private readonly List<ColumnCatalogEntry> _entries = [];
        private string _table = string.Empty;
        private int _order;

        public Builder Table(string table)
        {
            _table = table;
            _order = 0;
            return this;
        }

        public Builder Col(string name, CatalogValueType type, string labelEn, string labelFr, bool required = false)
        {
            _entries.Add(new ColumnCatalogEntry
            {
                TableName = _table,
                ColumnName = name,
                ValueType = type,
                LabelEn = labelEn,
                LabelFr = labelFr,
                IsRequired = required,
                IsCore = false,
                DisplayOrder = ++_order,
            });
            return this;
        }

        public Builder Ref(string name, string target, string labelEn, string labelFr)
        {
            _entries.Add(new ColumnCatalogEntry
            {
                TableName = _table,
                ColumnName = name,
                Kind = ColumnKind.Reference,
                ValueType = CatalogValueType.Uuid,
                ReferenceTarget = target,
                LabelEn = labelEn,
                LabelFr = labelFr,
                DisplayOrder = ++_order,
            });
            return this;
        }

        public Builder Computed(string name, string formula, string labelEn, string labelFr)
        {
            _entries.Add(new ColumnCatalogEntry
            {
                TableName = _table,
                ColumnName = name,
                Kind = ColumnKind.Computed,
                ValueType = CatalogValueType.Text,
                Formula = formula,
                LabelEn = labelEn,
                LabelFr = labelFr,
                DisplayOrder = ++_order,
            });
            return this;
        }

        public IReadOnlyList<ColumnCatalogEntry> Build() => _entries;
    }
}
