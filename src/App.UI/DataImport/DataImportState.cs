using System.Globalization;
using System.Timers;
using App.UI.Localization;

namespace App.UI.DataImport;

// ---- mock/source data model (vendor-neutral; mirrors the imported design) ----

/// <summary>A column in a source worksheet (name, inferred type, a sample value).</summary>
public sealed record SourceColumn(string Name, string Type, string Sample);

/// <summary>A source worksheet: its columns and a handful of preview rows.</summary>
public sealed record SourceSheet(string Name, IReadOnlyList<SourceColumn> Cols, IReadOnlyList<string[]> Data);

/// <summary>A target table column with constraint metadata (required / primary / surrogate / business key).</summary>
public sealed record TargetColumn(string Name, string Type, bool Required, bool Pk = false, string? Key = null);

/// <summary>A target table: its physical name and ordered columns.</summary>
public sealed record TargetTable(string Table, IReadOnlyList<TargetColumn> Cols);

/// <summary>An auto-suggested mapping for a source column (target column + 0..1 confidence).</summary>
public sealed record Suggestion(string Target, double Conf);

/// <summary>A simulated validation rejection / warning row.</summary>
public sealed record RejectRecord(string Sheet, string Row, string Col, string Value, string Reason, string Kind);

/// <summary>A recap detail row for one load status (inserted/updated/skipped/rejected).</summary>
public sealed record RecapRecord(string Sheet, string Row, string Key, string Name, string Detail);

/// <summary>The loaded source file's summary metadata (mock).</summary>
public sealed record SourceFile(string Name, string Size, string Type, int Sheets, int Rows, string Hash);

/// <summary>A saved mapping template.</summary>
public sealed record ImportTemplate(string Id, string Name, int Sheets, int Cols);

/// <summary>Per source-column mapping decision: chosen target + null handling.</summary>
public sealed class MappingCell
{
    public string Target { get; set; } = string.Empty;
    public string NullMode { get; set; } = "keep";
    public string Def { get; set; } = string.Empty;
}

/// <summary>One transform in a column's chain.</summary>
public sealed class Transform
{
    public string Type { get; set; } = string.Empty;
    public string Param { get; set; } = string.Empty;
}

/// <summary>CSV/Excel parsing options.</summary>
public sealed class ParseOptions
{
    public string Encoding { get; set; } = "UTF-8";
    public string Delimiter { get; set; } = ",";
    public string Quote { get; set; } = "\"";
    public string Header { get; set; } = "1";
    public string Decimal { get; set; } = ",";
}

/// <summary>Target connection (server / database / schema).</summary>
public sealed class Connection
{
    public string Server { get; set; } = "sql-prod-01";
    public string Database { get; set; } = "VentesDW";
    public string Schema { get; set; } = "ventes";
}

/// <summary>
/// Holds all state for the Data Import wizard ("DataBridge Import") — a port of the imported
/// <c>Data Import Tool.dc.html</c> design. Scoped per Blazor circuit. It carries the wizard step,
/// the (mock) source file + worksheets, column mappings, per-column transform chains, the simulated
/// validation/load results, saved templates, and a local light/dark theme. EN/FR comes from
/// <see cref="LanguageState"/>; this class only adds the feature-specific strings.
/// Raises <see cref="OnChange"/> so the component re-renders during the animated load run.
/// </summary>
public sealed class DataImportState : IDisposable
{
    private readonly LanguageState _lang;
    private System.Timers.Timer? _timer;
    private double _elapsedMs;

    public DataImportState(LanguageState lang) => _lang = lang;

    public event Action? OnChange;

    private void Notify() => OnChange?.Invoke();

    // ---------------- wizard state ----------------
    public int Step { get; private set; }
    public int MaxStep { get; private set; }
    public SourceFile? File { get; private set; }
    public ParseOptions Parse { get; } = new();
    public Connection Conn { get; } = new();
    public bool ConnOpen { get; private set; }
    public bool TplOpen { get; private set; }
    public string ActiveMapSheet { get; private set; } = "Clients";
    public Dictionary<string, Dictionary<string, MappingCell>>? Mappings { get; private set; }
    public string ActiveXfCol { get; private set; } = "Nom";
    public Dictionary<string, List<Transform>> Transforms { get; } = new(StringComparer.Ordinal);
    public string LoadMode { get; private set; } = "append";
    public bool Loading { get; private set; }
    public bool LoadDone { get; private set; }
    public bool Cancelled { get; private set; }
    public int Progress { get; private set; }
    public int RowsDone { get; private set; }
    public List<ImportTemplate> Templates { get; } = [new("t1", "Ventes — Clients & Produits", 2, 17)];
    public bool Drag { get; private set; }
    public bool RecapSaved { get; private set; }
    public string? RecapFilter { get; private set; }

    public bool IsFrench => _lang.IsFrench;

    // ---------------- totals (simulated) ----------------
    public int TotRead => 2481;
    public int TotValid => 2392;
    public int TotRejected => 64;
    public int TotWarnings => 25;
    public int TotInserted => 2310;
    public int TotUpdated => 82;
    public int TotSkipped => 25;

    // ---------------- source data ----------------
    public IReadOnlyDictionary<string, SourceSheet> Sheets { get; } = new Dictionary<string, SourceSheet>(StringComparer.Ordinal)
    {
        ["Clients"] = new("Clients",
        [
            new("Code Client", "text", "CLI-0001"),
            new("Nom", "text", "Müller"),
            new("Prénom", "text", "Élise"),
            new("Courriel", "text", "elise.muller@exemple.fr"),
            new("Ville", "text", "Genève"),
            new("Téléphone", "text", "+41 22 555 0147"),
            new("Date Inscription", "text", "2021-03-14"),
            new("Chiffre Affaires", "text", "12 450,50"),
            new("Actif", "boolean", "Oui"),
        ],
        [
            ["CLI-0001", "Müller", "Élise", "elise.muller@exemple.fr", "Genève", "+41 22 555 0147", "2021-03-14", "12 450,50", "Oui"],
            ["CLI-0002", "Lefèvre", "François", "f.lefevre@exemple.fr", "Montréal", "+1 514 555 0188", "2020-11-02", "8 800,00", "Oui"],
            ["CLI-0003", "Côté", "Joëlle", "joelle.cote@exemple.fr", "Trois-Rivières", "+1 819 555 0102", "2022-06-21", "23 110,75", "Non"],
            ["CLI-0004", "Lecœur", "Frédéric", "f.lecoeur@exemple.fr", "Bâle", "+41 61 555 0190", "2019-09-30", "4 500,00", "Oui"],
            ["CLI-0005", "Lemaître", "Anaïs", "anais.lemaitre@exemple.fr", "Nîmes", "+33 4 66 55 0173", "2023-01-08", "15 999,99", "Oui"],
            ["CLI-0006", "Deschâteau", "Hélène", "h.deschateau@exemple.fr", "Liège", "+32 4 555 0166", "2018-04-17", "31 240,10", "Oui"],
        ]),
        ["Produits"] = new("Produits",
        [
            new("Réf", "text", "PRD-1001"),
            new("Désignation", "text", "Café moulu Équateur"),
            new("Catégorie", "text", "Épicerie"),
            new("Prix Unitaire", "text", "6,90"),
            new("Devise", "text", "EUR"),
            new("Stock", "integer", "240"),
            new("TVA", "text", "5,5"),
            new("Disponible", "boolean", "Oui"),
        ],
        [
            ["PRD-1001", "Café moulu Équateur", "Épicerie", "6,90", "EUR", "240", "5,5", "Oui"],
            ["PRD-1002", "Bœuf séché fermier", "Boucherie", "18,40", "EUR", "75", "5,5", "Oui"],
            ["PRD-1003", "Thé vert Sencha", "Épicerie", "9,20", "EUR", "310", "5,5", "Oui"],
            ["PRD-1004", "Crème fraîche épaisse", "Crèmerie", "2,75", "EUR", "0", "5,5", "Non"],
            ["PRD-1005", "Pâté de campagne", "Traiteur", "7,60", "EUR", "128", "5,5", "Oui"],
            ["PRD-1006", "Sirop d’érable", "Épicerie", "11,30", "CAD", "64", "5,5", "Oui"],
        ]),
    };

    public IReadOnlyDictionary<string, TargetTable> Target { get; } = new Dictionary<string, TargetTable>(StringComparer.Ordinal)
    {
        ["Clients"] = new("clients",
        [
            new("client_id", "integer", true, Pk: true, Key: "surrogate"),
            new("client_code", "text", true, Key: "business"),
            new("last_name", "text", true),
            new("first_name", "text", false),
            new("email", "text", true),
            new("city", "text", false),
            new("phone", "text", false),
            new("signup_date", "date", true),
            new("revenue", "decimal", false),
            new("is_active", "boolean", false),
        ]),
        ["Produits"] = new("products",
        [
            new("product_id", "integer", true, Pk: true, Key: "surrogate"),
            new("product_ref", "text", true, Key: "business"),
            new("name", "text", true),
            new("category", "text", false),
            new("unit_price", "decimal", true),
            new("currency", "text", false),
            new("stock", "integer", false),
            new("vat_rate", "decimal", false),
            new("available", "boolean", false),
        ]),
    };

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, Suggestion>> Suggest { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, Suggestion>>(StringComparer.Ordinal)
        {
            ["Clients"] = new Dictionary<string, Suggestion>(StringComparer.Ordinal)
            {
                ["Code Client"] = new("client_code", 0.92), ["Nom"] = new("last_name", 0.97), ["Prénom"] = new("first_name", 0.95),
                ["Courriel"] = new("email", 0.74), ["Ville"] = new("city", 0.9), ["Téléphone"] = new("phone", 0.88),
                ["Date Inscription"] = new("signup_date", 0.83), ["Chiffre Affaires"] = new("revenue", 0.61), ["Actif"] = new("is_active", 0.79),
            },
            ["Produits"] = new Dictionary<string, Suggestion>(StringComparer.Ordinal)
            {
                ["Réf"] = new("product_ref", 0.86), ["Désignation"] = new("name", 0.72), ["Catégorie"] = new("category", 0.96),
                ["Prix Unitaire"] = new("unit_price", 0.81), ["Devise"] = new("currency", 0.93), ["Stock"] = new("stock", 0.98),
                ["TVA"] = new("vat_rate", 0.58), ["Disponible"] = new("available", 0.84),
            },
        };

    public IReadOnlyList<RejectRecord> Rejects { get; } =
    [
        new("Clients", "412", "email", "(empty)", "required_null", "err"),
        new("Clients", "588", "signup_date", "31/02/2022", "type_coerce", "err"),
        new("Clients", "903", "client_code", "CLI-0042", "dup_key", "err"),
        new("Clients", "1144", "last_name", "Van Den Heuvel-Maxim…", "length_overflow", "warn"),
        new("Produits", "77", "unit_price", "gratuit", "type_coerce", "err"),
        new("Produits", "201", "product_ref", "(empty)", "required_null", "err"),
        new("Produits", "356", "stock", "-12", "range_warn", "warn"),
        new("Produits", "489", "product_ref", "PRD-1002", "dup_key", "err"),
        new("Clients", "1502", "revenue", "12.450,50€", "type_coerce", "warn"),
        new("Produits", "640", "name", "(empty)", "required_null", "err"),
    ];

    public IReadOnlyDictionary<string, IReadOnlyList<RecapRecord>> RecapRows { get; } =
        new Dictionary<string, IReadOnlyList<RecapRecord>>(StringComparer.Ordinal)
        {
            ["inserted"] =
            [
                new("Clients", "1", "CLI-0001", "Müller, Élise", "Genève · 12 450,50 €"),
                new("Clients", "5", "CLI-0005", "Lemaître, Anaïs", "Nîmes · 15 999,99 €"),
                new("Produits", "1", "PRD-1001", "Café moulu Équateur", "Épicerie · 6,90 €"),
                new("Produits", "3", "PRD-1003", "Thé vert Sencha", "Épicerie · 9,20 €"),
            ],
            ["updated"] =
            [
                new("Clients", "2", "CLI-0002", "Lefèvre, François", "Courriel modifié"),
                new("Produits", "5", "PRD-1005", "Pâté de campagne", "Prix 7,40 → 7,60 €"),
                new("Produits", "6", "PRD-1006", "Sirop d’érable", "Stock 40 → 64"),
            ],
            ["skipped"] =
            [
                new("Clients", "88", "CLI-0044", "Bélanger, Noé", "Aucun changement détecté"),
                new("Produits", "120", "PRD-1108", "Crème fraîche", "Aucun changement détecté"),
            ],
            ["rejected"] =
            [
                new("Clients", "412", "—", "email", "Colonne requise vide"),
                new("Clients", "588", "—", "signup_date", "31/02/2022 · type invalide"),
                new("Clients", "903", "CLI-0042", "client_code", "Clé métier en double"),
                new("Produits", "77", "—", "unit_price", "« gratuit » · type invalide"),
                new("Produits", "489", "PRD-1002", "product_ref", "Clé métier en double"),
            ],
        };

    public static readonly string[] XfLibrary =
        ["trim", "upper", "lower", "title", "date", "number", "concat", "pad", "regex", "replace", "custom"];

    // ---------------- i18n (feature strings) ----------------
    private static readonly Dictionary<string, (string En, string Fr)> Tr = new(StringComparer.Ordinal)
    {
        ["appTitle"] = ("DataBridge Import", "DataBridge Import"), ["envBadge"] = ("Staging", "Pré-prod"),
        ["connection"] = ("CONNECTION", "CONNEXION"), ["templates"] = ("Templates", "Modèles"), ["theme"] = ("Theme", "Thème"),
        ["targetConnection"] = ("Target connection", "Connexion cible"), ["server"] = ("Server", "Serveur"),
        ["database"] = ("Database", "Base de données"), ["schema"] = ("Schema", "Schéma"),
        ["mappingTemplates"] = ("Mapping templates", "Modèles de correspondance"), ["saveCurrent"] = ("Save current", "Enregistrer"),
        ["apply"] = ("Apply", "Appliquer"), ["noTemplates"] = ("No saved templates yet. Configure a mapping and save it here.", "Aucun modèle enregistré. Configurez une correspondance et enregistrez-la ici."),
        ["back"] = ("Back", "Retour"), ["next"] = ("Next", "Suivant"), ["runValidation"] = ("Run validation", "Lancer la validation"),
        ["startLoad"] = ("Start load", "Démarrer le chargement"), ["finish"] = ("Finish", "Terminer"),
        ["stepSource"] = ("Source", "Source"), ["stepMapping"] = ("Mapping", "Correspondance"), ["stepTransform"] = ("Transform", "Transformation"),
        ["stepValidate"] = ("Validate", "Validation"), ["stepLoad"] = ("Load", "Chargement"), ["stepRecap"] = ("Recap", "Récapitulatif"),
        ["kSource"] = ("Step 1", "Étape 1"), ["kMapping"] = ("Step 2", "Étape 2"), ["kTransform"] = ("Step 3", "Étape 3"),
        ["kValidate"] = ("Step 4", "Étape 4"), ["kLoad"] = ("Step 5", "Étape 5"), ["kRecap"] = ("Step 6", "Étape 6"),
        ["s1Title"] = ("Source file & encoding", "Fichier source et encodage"),
        ["s1Sub"] = ("Drop a spreadsheet or CSV. We detect the format and let you tune how it is parsed.", "Déposez un classeur ou un CSV. Nous détectons le format et vous laissons régler l’analyse."),
        ["dropTitle"] = ("Drop your file here", "Déposez votre fichier ici"), ["dropOr"] = ("or", "ou"),
        ["dropBrowse"] = ("browse to upload", "parcourir pour téléverser"), ["maxSize"] = ("· up to 50 MB", "· jusqu’à 50 Mo"),
        ["useSample"] = ("Use sample workbook", "Utiliser le classeur exemple"), ["parsed"] = ("Parsed", "Analysé"),
        ["parsingOptions"] = ("Parsing options", "Options d’analyse"), ["encoding"] = ("Character encoding", "Encodage des caractères"),
        ["livePreview"] = ("Live preview", "Aperçu en direct"), ["delimiter"] = ("Delimiter", "Séparateur"),
        ["quoteChar"] = ("Quote character", "Caractère de citation"), ["headerRow"] = ("Header row", "Ligne d’en-tête"),
        ["decimalSep"] = ("Decimal separator", "Séparateur décimal"), ["noHeader"] = ("No header", "Aucun en-tête"),
        ["comma"] = ("Comma  (1,5)", "Virgule  (1,5)"), ["dot"] = ("Point  (1.5)", "Point  (1.5)"),
        ["worksheets"] = ("Worksheets", "Feuilles"),
        ["parsed_ok"] = ("Accented characters render correctly.", "Les caractères accentués s’affichent correctement."),
        ["parsed_bad"] = ("Accented characters look garbled — try UTF-8.", "Les caractères accentués sont corrompus — essayez UTF-8."),
        ["s1hint"] = ("Confirm encoding, then continue to mapping.", "Confirmez l’encodage, puis passez à la correspondance."),
        ["s2Title"] = ("Map worksheets & columns", "Correspondance des feuilles et colonnes"),
        ["s2Sub"] = ("Connect each source column to a target column. We auto-suggest by name similarity.", "Reliez chaque colonne source à une colonne cible. Suggestions automatiques par similarité de nom."),
        ["source"] = ("Source", "Source"), ["target"] = ("Target", "Cible"), ["ignore"] = ("— Ignore —", "— Ignorer —"),
        ["typeMismatch"] = ("Type mismatch", "Type incompatible"), ["requiredCols"] = ("unmapped required column(s)", "colonne(s) requise(s) non associée(s)"),
        ["allMapped"] = ("All required columns mapped", "Toutes les colonnes requises sont associées"),
        ["cols"] = ("cols", "col."), ["rows"] = ("rows", "lignes"),
        ["pk"] = ("PK", "CP"), ["sk"] = ("surrogate", "substitution"), ["bk"] = ("business", "métier"), ["req"] = ("required", "requis"),
        ["s3Title"] = ("Transform columns", "Transformer les colonnes"),
        ["s3Sub"] = ("Build chains of transforms per column. Preview runs live on the first rows.", "Construisez des chaînes de transformations par colonne. L’aperçu s’exécute en direct."),
        ["worksheetSel"] = ("Worksheet → target table", "Feuille → table cible"), ["column"] = ("Column", "Colonne"),
        ["transformLib"] = ("Transform library", "Bibliothèque de transformations"), ["activeChain"] = ("Active chain", "Chaîne active"),
        ["noTransforms"] = ("No transforms on this column yet. Add one from the library.", "Aucune transformation sur cette colonne. Ajoutez-en une depuis la bibliothèque."),
        ["beforeAfter"] = ("Before / after preview", "Aperçu avant / après"), ["before"] = ("Before", "Avant"), ["after"] = ("After", "Après"),
        ["xfTrim"] = ("Trim", "Élaguer"), ["xfUpper"] = ("UPPERCASE", "MAJUSCULES"), ["xfLower"] = ("lowercase", "minuscules"),
        ["xfTitle"] = ("Title Case", "Casse Titre"), ["xfRegex"] = ("Regex replace", "Remplacer (regex)"), ["xfDate"] = ("Date reformat", "Reformater date"),
        ["xfNumber"] = ("Parse number", "Analyser nombre"), ["xfConcat"] = ("Add prefix", "Ajouter préfixe"), ["xfPad"] = ("Pad left", "Compléter à gauche"),
        ["xfReplace"] = ("Replace from list", "Remplacer depuis liste"), ["xfCustom"] = ("Custom expression", "Expression personnalisée"),
        ["s4Title"] = ("Validate (dry run)", "Validation (test à blanc)"),
        ["s4Sub"] = ("We simulate the insert against target constraints. Nothing is committed.", "Nous simulons l’insertion selon les contraintes cibles. Rien n’est validé."),
        ["rowsRead"] = ("Rows read", "Lignes lues"), ["rowsValid"] = ("Rows valid", "Lignes valides"),
        ["rowsRejected"] = ("Rows rejected", "Lignes rejetées"), ["warnings"] = ("Warnings", "Avertissements"),
        ["rejectionLog"] = ("Rejection log", "Journal des rejets"), ["exportLog"] = ("Export", "Exporter"),
        ["vRow"] = ("Row", "Ligne"), ["vColumn"] = ("Column", "Colonne"), ["vValue"] = ("Value", "Valeur"),
        ["vReason"] = ("Reason", "Motif"), ["vSheet"] = ("Sheet", "Feuille"),
        ["loadModeTitle"] = ("Load mode", "Mode de chargement"), ["constraintChecks"] = ("Constraint checks", "Contrôles de contraintes"),
        ["failed"] = ("issues", "problèmes"),
        ["modeAppend"] = ("Append", "Ajouter"), ["modeAppendD"] = ("Add all valid rows; existing data untouched.", "Ajoute toutes les lignes valides ; données existantes intactes."),
        ["modeTruncate"] = ("Truncate + Insert", "Vider + Insérer"), ["modeTruncateD"] = ("Empty the target table first, then insert.", "Vide d’abord la table cible, puis insère."),
        ["modeUpsert"] = ("Upsert / Merge", "Fusion / Upsert"), ["modeUpsertD"] = ("Update rows matching the key, insert the rest.", "Met à jour les lignes correspondant à la clé, insère le reste."),
        ["modeScd"] = ("SCD update", "Mise à jour SCD"), ["modeScdD"] = ("Dimension-aware: close changed rows, insert new versions.", "Dimensionnel : clôt les lignes modifiées, insère de nouvelles versions."),
        ["s5Title"] = ("Load", "Chargement"),
        ["s5Sub"] = ("Review what will happen, then run the load. You can cancel mid-stream.", "Vérifiez ce qui va se passer, puis lancez le chargement. Annulable en cours."),
        ["willHappen"] = ("What will happen", "Ce qui va se passer"), ["confirmLoad"] = ("Confirm & start load", "Confirmer et démarrer"),
        ["loadingRows"] = ("Loading rows…", "Chargement des lignes…"), ["cancel"] = ("Cancel", "Annuler"), ["cancelled"] = ("Cancelled", "Annulé"),
        ["loadComplete"] = ("Load complete", "Chargement terminé"), ["loadPartial"] = ("Completed with rejections", "Terminé avec des rejets"),
        ["elapsed"] = ("Elapsed", "Écoulé"), ["throughput"] = ("Throughput", "Débit"), ["viewRecap"] = ("View recap →", "Voir le récapitulatif →"),
        ["rowsToLoad"] = ("valid rows to load", "lignes valides à charger"), ["preflight"] = ("Pre-flight checks passed", "Contrôles préalables réussis"),
        ["s6Title"] = ("Load recap", "Récapitulatif du chargement"),
        ["s6Sub"] = ("Audit summary of the completed load. Export, save as template, or start again.", "Synthèse d’audit du chargement terminé. Exportez, enregistrez en modèle ou recommencez."),
        ["inserted"] = ("Inserted", "Insérées"), ["updated"] = ("Updated", "Mises à jour"), ["skipped"] = ("Skipped", "Ignorées"), ["rejected"] = ("Rejected", "Rejetées"),
        ["rowsByStatus"] = ("Rows by status", "Lignes par statut"), ["rowsByTable"] = ("Rows per target table", "Lignes par table cible"),
        ["runDetails"] = ("Run details", "Détails de l’exécution"), ["duration"] = ("Duration", "Durée"),
        ["fileName"] = ("File", "Fichier"), ["sourceHash"] = ("Source hash", "Empreinte source"), ["encodingUsed"] = ("Encoding", "Encodage"),
        ["targetTables"] = ("Target table(s)", "Table(s) cible"), ["loadModeL"] = ("Load mode", "Mode"), ["timestamp"] = ("Timestamp", "Horodatage"),
        ["user"] = ("User", "Utilisateur"), ["exportRejects"] = ("Export rejection log", "Exporter le journal des rejets"),
        ["saveAsTemplate"] = ("Save as template", "Enregistrer en modèle"), ["startNew"] = ("Start a new load", "Nouveau chargement"),
        ["templateSaved"] = ("Template saved ✓", "Modèle enregistré ✓"),
        ["navImport"] = ("Import", "Importer"),
    };

    /// <summary>Feature string lookup for the current language (falls back to the key).</summary>
    public string T(string key) => Tr.TryGetValue(key, out (string En, string Fr) s) ? (IsFrench ? s.Fr : s.En) : key;

    public string FmtInt(int n) => n.ToString("N0", IsFrench ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.GetCultureInfo("en-US"));

    // ---------------- popovers ----------------
    public void ToggleConn() { ConnOpen = !ConnOpen; TplOpen = false; Notify(); }
    public void ToggleTpl() { TplOpen = !TplOpen; ConnOpen = false; Notify(); }
    public void ClosePops() { ConnOpen = false; TplOpen = false; Notify(); }

    public void SetConn(string key, string value)
    {
        switch (key)
        {
            case "server": Conn.Server = value; break;
            case "database": Conn.Database = value; break;
            case "schema": Conn.Schema = value; break;
        }

        Notify();
    }

    public void SetParse(string key, string value)
    {
        switch (key)
        {
            case "encoding": Parse.Encoding = value; break;
            case "delimiter": Parse.Delimiter = value; break;
            case "quote": Parse.Quote = value; break;
            case "header": Parse.Header = value; break;
            case "decimal": Parse.Decimal = value; break;
        }

        Notify();
    }

    // ---------------- file ----------------
    private Dictionary<string, Dictionary<string, MappingCell>> InitMappings()
    {
        Dictionary<string, Dictionary<string, MappingCell>> m = new(StringComparer.Ordinal);
        foreach ((string sheet, IReadOnlyDictionary<string, Suggestion> cols) in Suggest)
        {
            Dictionary<string, MappingCell> cells = new(StringComparer.Ordinal);
            foreach ((string col, Suggestion sug) in cols)
            {
                cells[col] = new MappingCell { Target = sug.Target, NullMode = "keep" };
            }

            m[sheet] = cells;
        }

        return m;
    }

    public void LoadSample()
    {
        File = new SourceFile("ventes_export_2026Q2.xlsx", "1.8 MB", "Excel (.xlsx)", 2, 2481, "a3f9-7c2e-d104");
        Mappings = InitMappings();
        Notify();
    }

    public void ClearFile() { File = null; Step = 0; MaxStep = 0; Notify(); }
    public void SetDrag(bool dragging) { if (Drag != dragging) { Drag = dragging; Notify(); } }

    // ---------------- nav ----------------
    public void GoToStep(int i)
    {
        if (i <= MaxStep) { Step = i; ConnOpen = false; TplOpen = false; Notify(); }
    }

    public void GoNext()
    {
        if (Step >= 5) { return; }
        if (Step == 0 && File is null) { return; }
        Step++;
        MaxStep = Math.Max(MaxStep, Step);
        ConnOpen = false; TplOpen = false;
        Notify();
    }

    public void GoBack() { Step = Math.Max(0, Step - 1); ConnOpen = false; TplOpen = false; Notify(); }

    // ---------------- mapping ----------------
    public void SetMapSheet(string name) { ActiveMapSheet = name; Notify(); }

    public void SetMapTarget(string src, string value)
    {
        Mappings ??= InitMappings();
        if (!Mappings.TryGetValue(ActiveMapSheet, out Dictionary<string, MappingCell>? cells))
        {
            cells = new Dictionary<string, MappingCell>(StringComparer.Ordinal);
            Mappings[ActiveMapSheet] = cells;
        }

        if (!cells.TryGetValue(src, out MappingCell? cell))
        {
            cell = new MappingCell();
            cells[src] = cell;
        }

        cell.Target = value;
        Notify();
    }

    public MappingCell MapCell(string sheet, string src)
        => Mappings is not null && Mappings.TryGetValue(sheet, out Dictionary<string, MappingCell>? cells) && cells.TryGetValue(src, out MappingCell? c)
            ? c
            : new MappingCell { Target = string.Empty };

    // ---------------- transforms ----------------
    public void SetXfCol(string col) { ActiveXfCol = col; Notify(); }

    public void SetXfSheet(string name)
    {
        if (Sheets.TryGetValue(name, out SourceSheet? sh))
        {
            ActiveMapSheet = name;
            ActiveXfCol = sh.Cols[0].Name;
            Notify();
        }
    }

    public List<Transform> Chain(string col) => Transforms.TryGetValue(col, out List<Transform>? c) ? c : [];

    public void AddXf(string type)
    {
        if (!Transforms.TryGetValue(ActiveXfCol, out List<Transform>? arr))
        {
            arr = [];
            Transforms[ActiveXfCol] = arr;
        }

        arr.Add(new Transform { Type = type, Param = DefaultParam(type) });
        Notify();
    }

    public void RemoveXf(int idx)
    {
        if (Transforms.TryGetValue(ActiveXfCol, out List<Transform>? arr) && idx >= 0 && idx < arr.Count)
        {
            arr.RemoveAt(idx);
            Notify();
        }
    }

    private static string DefaultParam(string t) => t switch
    {
        "concat" => "CLI-",
        "pad" => "6",
        "regex" => "\\s+ → _",
        "date" => "dd/MM/yyyy",
        "replace" => "Oui→true",
        "custom" => "value.trim()",
        _ => string.Empty,
    };

    public string ApplyXf(string? val, Transform t)
    {
        if (val is null) { return string.Empty; }
        string v = val;
        switch (t.Type)
        {
            case "trim": return v.Trim();
            case "upper": return v.ToUpperInvariant();
            case "lower": return v.ToLowerInvariant();
            case "title": return TitleCase(v);
            case "number": return v.Replace(" ", string.Empty).Replace(",", ".");
            case "concat": return (t.Param ?? string.Empty) + v;
            case "pad":
                int n = int.TryParse(t.Param, out int p) ? p : 6;
                return v.PadLeft(n, '0');
            case "date":
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(v, @"(\d{4})-(\d{2})-(\d{2})");
                return m.Success ? $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value}" : v;
            case "replace": return v == "Oui" ? "true" : v == "Non" ? "false" : v;
            case "regex": return System.Text.RegularExpressions.Regex.Replace(v, @"\s+", "_");
            case "custom": return v.Trim();
            default: return v;
        }
    }

    private static string TitleCase(string v)
        => System.Text.RegularExpressions.Regex.Replace(
            v.ToLowerInvariant(), @"(^|\s|-)([a-zà-ÿ])",
            mm => mm.Groups[1].Value + mm.Groups[2].Value.ToUpperInvariant());

    // ---------------- load mode + run ----------------
    public void SetMode(string mode) { LoadMode = mode; Notify(); }

    public string ModeName(string mode) => mode switch
    {
        "truncate" => T("modeTruncate"),
        "upsert" => T("modeUpsert"),
        "scd" => T("modeScd"),
        _ => T("modeAppend"),
    };

    public void StartLoad()
    {
        StopTimer();
        Loading = true; LoadDone = false; Cancelled = false; Progress = 0; RowsDone = 0;
        _elapsedMs = 0;
        const double dur = 4200;
        _timer = new System.Timers.Timer(60) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
        {
            _elapsedMs += 60;
            double prog = Math.Min(1, _elapsedMs / dur);
            double eased = 1 - Math.Pow(1 - prog, 2);
            Progress = (int)Math.Round(eased * 100);
            RowsDone = (int)Math.Round(eased * TotValid);
            if (prog >= 1)
            {
                StopTimer();
                Loading = false; LoadDone = true; Progress = 100; RowsDone = TotValid;
            }

            Notify();
        };
        _timer.Start();
        Notify();
    }

    public void CancelLoad() { StopTimer(); Loading = false; Cancelled = true; Notify(); }

    public void GotoRecap() { Step = 5; MaxStep = Math.Max(MaxStep, 5); Notify(); }

    private void StopTimer()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }
    }

    // ---------------- templates / recap ----------------
    public void SaveTpl()
    {
        string id = "t" + (Templates.Count + 1);
        Templates.Add(new ImportTemplate(id, $"Modèle {Templates.Count + 1} · {Conn.Schema}", 2, 17));
        RecapSaved = true;
        Notify();
    }

    public void ApplyTpl() { ConnOpen = false; TplOpen = false; Notify(); }

    public void SetRecapFilter(string key) { RecapFilter = RecapFilter == key ? null : key; Notify(); }
    public void ClearRecapFilter() { RecapFilter = null; Notify(); }

    public void StartNew()
    {
        StopTimer();
        File = null; Step = 0; MaxStep = 0; Loading = false; LoadDone = false; Cancelled = false;
        Progress = 0; RowsDone = 0; Transforms.Clear(); Mappings = null; RecapSaved = false; RecapFilter = null;
        Notify();
    }

    // ---------------- encoding garble (preview) ----------------
    private static readonly (string From, string To)[] Garbles =
    [
        ("é", "Ã©"), ("è", "Ã¨"), ("ê", "Ãª"), ("à", "Ã "), ("â", "Ã¢"), ("ç", "Ã§"), ("î", "Ã®"), ("ï", "Ã¯"),
        ("ô", "Ã´"), ("û", "Ã»"), ("ù", "Ã¹"), ("ë", "Ã«"), ("œ", "Å“"), ("É", "Ã‰"), ("Ü", "Ã"), ("ü", "Ã¼"),
    ];

    public bool IsBadEncoding => Parse.Encoding is "Windows-1252" or "ISO-8859-1";

    public string Garble(string s)
    {
        foreach ((string from, string to) in Garbles)
        {
            s = s.Replace(from, to);
        }

        return s;
    }

    // ---------------- transform labels/icons ----------------
    public string XfLabel(string type) => type switch
    {
        "trim" => T("xfTrim"), "upper" => T("xfUpper"), "lower" => T("xfLower"), "title" => T("xfTitle"),
        "date" => T("xfDate"), "number" => T("xfNumber"), "concat" => T("xfConcat"), "pad" => T("xfPad"),
        "regex" => T("xfRegex"), "replace" => T("xfReplace"), "custom" => T("xfCustom"), _ => type,
    };

    public static string XfIcon(string type) => type switch
    {
        "trim" => "⤧", "upper" => "A", "lower" => "a", "title" => "Aa", "date" => "◷", "number" => "#",
        "concat" => "+", "pad" => "⊞", "regex" => ".*", "replace" => "⇄", "custom" => "ƒ", _ => "•",
    };

    public void Dispose() => StopTimer();
}
