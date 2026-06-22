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
            .Col("application_id", CatalogValueType.Text, "Application", "Application")
            .Col("name", CatalogValueType.Text, "Name", "Nom", required: true)
            .Col("description", CatalogValueType.Text, "Description", "Description")
            .Col("type", CatalogValueType.Text, "Type", "Type")
            .Col("bloc", CatalogValueType.Text, "Bloc", "Bloc")
            .Col("frequency", CatalogValueType.Text, "Frequency", "Fréquence")
            .Col("full_name", CatalogValueType.Text, "Full name", "Nom complet")
            .Col("bronze_path", CatalogValueType.Text, "Bronze path", "Répertoire brute")
            .Col("silver_path", CatalogValueType.Text, "Silver path", "Répertoire standardisé")
            .Col("gold_path", CatalogValueType.Text, "Gold path", "Répertoire Valo (Gold)")
            .Col("status", CatalogValueType.Text, "Status", "Statut");

        b.Table(TableNames.DictionaryEntry)
            .Col("source_id", CatalogValueType.Text, "Source", "Source")
            .Col("column_name", CatalogValueType.Text, "Column", "Colonne", required: true)
            .Col("ordinal", CatalogValueType.Integer, "Order", "Ordre")
            .Col("data_type", CatalogValueType.Text, "Data type", "Type de données")
            .Col("is_primary_key", CatalogValueType.Boolean, "Primary key", "Clé primaire")
            .Col("is_nullable", CatalogValueType.Boolean, "Nullable", "Nullable")
            .Col("business_name", CatalogValueType.Text, "Business name", "Nom affaire")
            .Col("description", CatalogValueType.Text, "Description", "Description")
            .Col("classification_prp", CatalogValueType.Text, "PRP", "PRP")
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
            .Col("target_entry", CatalogValueType.Text, "Target", "Cible")
            .Col("kind", CatalogValueType.Text, "Kind", "Type")
            .Col("expression", CatalogValueType.Text, "Expression", "Expression")
            .Col("is_tokenized", CatalogValueType.Boolean, "Tokenized", "Tokenisé")
            .Col("notes", CatalogValueType.Text, "Notes", "Notes");

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

        public IReadOnlyList<ColumnCatalogEntry> Build() => _entries;
    }
}
