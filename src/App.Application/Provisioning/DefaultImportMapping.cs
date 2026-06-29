using App.Application.Importing;
using App.Domain.Entities;

namespace App.Application.Provisioning;

/// <summary>
/// The built-in worksheet→table mapping for the team's workbook, mirroring
/// <c>build/import-mapping.json</c>. The in-app Data Import wizard uses this so it can import the
/// standard workbook with no external mapping file; the CLI can still be pointed at an edited
/// <c>import-mapping.json</c>. Only listed columns import; derived/computed columns are omitted.
/// </summary>
public static class DefaultImportMapping
{
    public static ImportMapping Mapping { get; } = new()
    {
        Worksheets =
        [
            new WorksheetMapping
            {
                Worksheet = "Apps",
                Table = TableNames.Application,
                NaturalKey = ["app_code"],
                Columns = new Dictionary<string, string>
                {
                    ["AppCode"] = "app_code",
                    ["Nom GDM"] = "name_gdm",
                    ["Nom VA360"] = "name_va360",
                    ["Nom Abrégé"] = "short_name",
                    ["Répertoire Valo (Gold)"] = "gold_root_path",
                    ["Description"] = "description",
                    ["Responsable TI"] = "responsible_it",
                    ["Responsable Affaire"] = "responsible_business",
                },
            },
            new WorksheetMapping
            {
                Worksheet = "Config",
                Table = TableNames.LookupValue,
                NaturalKey = ["lookup_type", "value"],
                Columns = new Dictionary<string, string>
                {
                    ["Type"] = "lookup_type",
                    ["Value"] = "value",
                    ["DescriptionEN"] = "description_en",
                    ["DescriptionFR"] = "description_fr",
                },
            },
            new WorksheetMapping
            {
                Worksheet = "Classification",
                Table = TableNames.Classification,
                NaturalKey = ["prp"],
                Columns = new Dictionary<string, string>
                {
                    ["ClassificationPRP"] = "prp",
                    ["ClassificationAcces"] = "access_901",
                    ["ClassificationDivulgation"] = "disclosure_902",
                },
            },
            new WorksheetMapping
            {
                Worksheet = "Source",
                Table = TableNames.DataSource,
                NaturalKey = ["name"],
                Columns = new Dictionary<string, string>
                {
                    ["Nom"] = "name",
                    ["Description"] = "description",
                    ["Type"] = "type",
                    ["Bloc"] = "bloc",
                    ["Fréquence"] = "frequency",
                    ["Nom complet"] = "full_name",
                    ["Répertoire Brute"] = "bronze_path",
                    ["Répertoire Standardisé"] = "silver_path",
                    ["Répertoire Valo (Gold)"] = "gold_path",
                    ["Statut"] = "status",
                    ["Code applicatif du système"] = "application_id",
                },
                References = new Dictionary<string, ColumnReference>
                {
                    ["application_id"] = new("application", "app_code"),
                },
            },
            new WorksheetMapping
            {
                Worksheet = "Dictionnary",
                Table = TableNames.DictionaryEntry,
                NaturalKey = ["column_name"],
                Columns = new Dictionary<string, string>
                {
                    ["Colonne"] = "column_name",
                    ["Ordre"] = "ordinal",
                    ["Type de donnees"] = "data_type",
                    ["Nom Affaire"] = "business_name",
                    ["Description"] = "description",
                    ["Langue"] = "language",
                    ["Statut"] = "status",
                    ["PRP - ID"] = "classification_id",
                },
                References = new Dictionary<string, ColumnReference>
                {
                    ["classification_id"] = new("classification", "prp"),
                },
            },
            new WorksheetMapping
            {
                Worksheet = "Mapping",
                Table = TableNames.Mapping,
                NaturalKey = [],
                ExpressionColumn = "expression",
                Columns = new Dictionary<string, string>
                {
                    ["Cible - Règles de transformation"] = "expression",
                    ["Notes"] = "notes",
                },
            },
        ],
    };
}
