using App.Application.Abstractions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Domain.Catalog;
using App.Domain.Data;
using App.UI.Localization;
using Microsoft.Extensions.Logging;

namespace App.UI.DataImport;

/// <summary>One source column and where the mapping sends it (or that it is ignored).</summary>
public sealed record MapRowVm(string Source, string? Target, string TargetType, bool Required, bool IsReference, bool IsExpression, string? Sample);

/// <summary>A worksheet's import summary for the source/mapping steps.</summary>
public sealed record WorksheetVm(string Name, string Table, bool Present, int Rows, int MappedColumns, int SourceColumns, IReadOnlyList<string> UnmappedHeaders);

/// <summary>A worksheet's dry-run validation outcome (computed without writing anything).</summary>
public sealed record ValidationVm(string Worksheet, string Table, int Rows, int WouldImport, int WouldSkip, IReadOnlyList<string> MissingRequired, IReadOnlyList<string> UnmappedHeaders);

/// <summary>
/// Orchestrates the in-app Data Import wizard against the <b>real</b> local database: an uploaded
/// workbook is parsed by <see cref="IWorkbookReader"/> using the built-in
/// <see cref="DefaultImportMapping"/>, previewed and dry-run validated against the column catalog, then
/// loaded through the shared <see cref="ImportEngine"/> into <see cref="ILocalStore"/> as one reviewable
/// Import change set. The same engine the CLI uses — no mock data. Scoped per Blazor circuit.
/// </summary>
public sealed class DataImportState
{
    private readonly LanguageState _lang;
    private readonly ICatalog _catalog;
    private readonly ImportEngine _engine;
    private readonly IWorkbookReader _reader;
    private readonly ICurrentUser _user;
    private readonly ILogger<DataImportState>? _logger;

    public DataImportState(LanguageState lang, ICatalog catalog, ImportEngine engine, IWorkbookReader reader, ICurrentUser user, ILogger<DataImportState>? logger = null)
    {
        _lang = lang;
        _catalog = catalog;
        _engine = engine;
        _reader = reader;
        _user = user;
        _logger = logger;
    }

    public event Action? OnChange;

    private void Notify() => OnChange?.Invoke();

    public bool IsFrench => _lang.IsFrench;

    /// <summary>The worksheet→table mapping that drives the import (the team workbook layout).</summary>
    public ImportMapping Mapping { get; } = DefaultImportMapping.Mapping;

    public int Step { get; private set; }
    public int MaxStep { get; private set; }
    public const int StepCount = 5; // Source, Mapping, Validate, Load, Recap

    public string? FileName { get; private set; }
    public long FileSize { get; private set; }
    public IReadOnlyList<WorksheetData>? Parsed { get; private set; }
    public string? ParseError { get; private set; }
    public bool Busy { get; private set; }

    public bool Loading { get; private set; }
    public bool Done { get; private set; }
    public bool Cancelled { get; private set; }
    public int Progress { get; private set; }
    public int RowsDone { get; private set; }
    public ImportReport? Report { get; private set; }
    public string? LoadError { get; private set; }
    private CancellationTokenSource? _cts;

    /// <summary>Upload guard rails: reject pathological files before they can block the circuit.</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;
    public const int MaxRows = 100_000;

    public bool HasFile => Parsed is not null;

    /// <summary>Worksheets that exist in the uploaded workbook with at least one data row.</summary>
    public IEnumerable<WorksheetData> PopulatedSheets =>
        Parsed?.Where(s => s.Rows.Count > 0) ?? [];

    public int TotalRows => PopulatedSheets.Sum(s => s.Rows.Count);

    public IReadOnlyList<string> TargetTables => Mapping.Worksheets.Select(w => w.Table).Distinct().ToList();

    // ---------------- file load ----------------
    public async Task LoadFileAsync(string name, long size, Stream content)
    {
        if (size > MaxFileBytes)
        {
            ParseError = T("fileTooLarge");
            Parsed = null;
            Notify();
            return;
        }

        Busy = true;
        ParseError = null;
        Notify();
        try
        {
            // Copy to a seekable stream (InputFile streams are forward-only) and parse.
            using MemoryStream buffer = new();
            await content.CopyToAsync(buffer);
            buffer.Position = 0;

            IReadOnlyList<WorksheetData> parsed = _reader.Read(buffer, Mapping);
            if (parsed.All(s => s.Rows.Count == 0))
            {
                ParseError = IsFrench
                    ? "Aucune feuille reconnue. Vérifiez que le classeur suit la structure attendue (Apps, Source, Dictionnary…)."
                    : "No recognized worksheets. Check that the workbook follows the expected layout (Apps, Source, Dictionnary…).";
                Parsed = null;
            }
            else if (parsed.Where(s => s.Rows.Count > 0).Sum(s => s.Rows.Count) > MaxRows)
            {
                ParseError = T("tooManyRows");
                Parsed = null;
            }
            else
            {
                Parsed = parsed;
                FileName = name;
                FileSize = size;
            }
        }
        catch (Exception ex)
        {
            ParseError = (IsFrench ? "Échec de lecture du classeur : " : "Could not read the workbook: ") + ex.Message;
            Parsed = null;
        }
        finally
        {
            Busy = false;
            Notify();
        }
    }

    /// <summary>Rejects a file the host already knows is too large (without opening its stream).</summary>
    public void RejectTooLarge()
    {
        ParseError = T("fileTooLarge");
        Parsed = null;
        Notify();
    }

    public void ClearFile()
    {
        Parsed = null; FileName = null; FileSize = 0; ParseError = null;
        Step = 0; MaxStep = 0; Loading = false; Done = false; Cancelled = false;
        Progress = 0; RowsDone = 0; Report = null; LoadError = null;
        Notify();
    }

    // ---------------- navigation ----------------
    public void GoToStep(int i)
    {
        if (i >= 0 && i <= MaxStep)
        {
            Step = i;
            Notify();
        }
    }

    public void GoNext()
    {
        if (Step >= StepCount - 1) { return; }
        if (Step == 0 && !HasFile) { return; }
        Step++;
        MaxStep = Math.Max(MaxStep, Step);
        Notify();
    }

    public void GoBack()
    {
        Step = Math.Max(0, Step - 1);
        Notify();
    }

    // ---------------- run the import ----------------
    /// <summary>
    /// Runs the import off the UI thread (so a large workbook never freezes the circuit), reporting
    /// progress and honouring <see cref="Cancel"/>. Rows already written stay on cancel (idempotent
    /// upsert), so the partial result is surfaced and a re-run completes it.
    /// </summary>
    public async Task RunImportAsync()
    {
        if (Parsed is null || Loading) { return; }

        Loading = true;
        LoadError = null;
        Cancelled = false;
        Progress = 0;
        RowsDone = 0;
        _cts = new CancellationTokenSource();
        Notify();

        int total = TotalRows;
        int lastPct = -1;
        // Throttle UI updates to whole-percent changes (~100 renders max, not one per row).
        Progress<int> progress = new(done =>
        {
            RowsDone = done;
            int pct = total > 0 ? (int)Math.Min(100, done * 100L / total) : 100;
            if (pct != lastPct)
            {
                lastPct = pct;
                Progress = pct;
                Notify();
            }
        });

        try
        {
            CancellationToken token = _cts.Token;
            IReadOnlyList<WorksheetData> sheets = Parsed;
            Report = await Task.Run(() => _engine.Run(Mapping, sheets, _user.Name, token, progress), token);

            if (token.IsCancellationRequested)
            {
                Cancelled = true;
                _logger?.LogWarning("Data import cancelled by {User} ({File}): {Created} created, {Updated} updated before cancel.",
                    _user.Name, FileName, Report.TotalCreated, Report.TotalUpdated);
            }
            else
            {
                Done = true;
                Step = StepCount - 1; // jump to recap
                MaxStep = Math.Max(MaxStep, Step);
                _logger?.LogInformation("Data import by {User} ({File}): {Created} created, {Updated} updated, {Skipped} skipped.",
                    _user.Name, FileName, Report.TotalCreated, Report.TotalUpdated, Report.TotalSkipped);
            }
        }
        catch (OperationCanceledException)
        {
            Cancelled = true;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            _logger?.LogError(ex, "Data import by {User} ({File}) failed.", _user.Name, FileName);
        }
        finally
        {
            Loading = false;
            _cts.Dispose();
            _cts = null;
            Notify();
        }
    }

    /// <summary>Requests cancellation of an in-progress import.</summary>
    public void Cancel() => _cts?.Cancel();

    /// <summary>Jumps to the recap after a cancelled run to review the partial result.</summary>
    public void GotoRecap()
    {
        Step = StepCount - 1;
        MaxStep = Math.Max(MaxStep, Step);
        Notify();
    }

    public void StartNew() => ClearFile();

    // ---------------- catalog-driven derivations ----------------
    private IReadOnlyList<ColumnCatalogEntry> TableColumns(string table) => _catalog.GetForTable(table);

    private ColumnCatalogEntry? Col(string table, string column)
        => TableColumns(table).FirstOrDefault(c => c.ColumnName == column);

    public WorksheetData? SheetFor(string worksheet) => Parsed?.FirstOrDefault(s => s.Worksheet == worksheet);

    /// <summary>Summary card per worksheet for the Source step (only worksheets present in the file).</summary>
    public IReadOnlyList<WorksheetVm> Worksheets()
    {
        List<WorksheetVm> list = [];
        foreach (WorksheetMapping ws in Mapping.Worksheets)
        {
            WorksheetData? sheet = SheetFor(ws.Worksheet);
            if (sheet is null || sheet.Rows.Count == 0)
            {
                continue;
            }

            HashSet<string> headers = SheetHeaders(sheet);
            int mapped = ws.Columns.Keys.Count(headers.Contains);
            List<string> unmapped = headers.Where(h => !ws.Columns.ContainsKey(h)).OrderBy(h => h, StringComparer.Ordinal).ToList();
            list.Add(new WorksheetVm(ws.Worksheet, ws.Table, true, sheet.Rows.Count, mapped, headers.Count, unmapped));
        }

        return list;
    }

    /// <summary>The column-by-column mapping rows for one worksheet (source header → target column).</summary>
    public IReadOnlyList<MapRowVm> MapRows(string worksheet)
    {
        WorksheetMapping? ws = Mapping.Worksheets.FirstOrDefault(w => w.Worksheet == worksheet);
        WorksheetData? sheet = SheetFor(worksheet);
        if (ws is null || sheet is null)
        {
            return [];
        }

        List<MapRowVm> rows = [];
        foreach (string header in SheetHeaders(sheet))
        {
            ws.Columns.TryGetValue(header, out string? target);
            ColumnCatalogEntry? entry = target is null ? null : Col(ws.Table, target);
            bool isRef = target is not null && ws.References.ContainsKey(target);
            bool isExpr = target is not null && ws.ExpressionColumn == target;
            string? sample = sheet.Rows.Select(r => r.GetValueOrDefault(header)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            rows.Add(new MapRowVm(
                header,
                target,
                entry?.ValueType.ToString().ToLowerInvariant() ?? string.Empty,
                entry?.IsRequired ?? false,
                isRef,
                isExpr,
                sample));
        }

        return rows;
    }

    /// <summary>Dry-run validation per worksheet: how many rows would import vs. be skipped, and why.</summary>
    public IReadOnlyList<ValidationVm> Validate()
    {
        List<ValidationVm> list = [];
        foreach (WorksheetMapping ws in Mapping.Worksheets)
        {
            WorksheetData? sheet = SheetFor(ws.Worksheet);
            if (sheet is null || sheet.Rows.Count == 0)
            {
                continue;
            }

            // Required catalog columns and which source header (if any) feeds each.
            List<ColumnCatalogEntry> required = TableColumns(ws.Table)
                .Where(c => c.IsRequired && c.Kind == ColumnKind.Scalar)
                .ToList();
            Dictionary<string, string?> headerForColumn = ws.Columns.ToDictionary(kv => kv.Value, kv => (string?)kv.Key, StringComparer.Ordinal);

            // A required target column with no source header mapped means every row is skipped.
            List<string> missingRequired = required
                .Where(c => !headerForColumn.ContainsKey(c.ColumnName))
                .Select(c => c.ColumnName)
                .ToList();

            int wouldSkip;
            if (missingRequired.Count > 0)
            {
                wouldSkip = sheet.Rows.Count;
            }
            else
            {
                wouldSkip = sheet.Rows.Count(row => required.Any(c =>
                    headerForColumn.TryGetValue(c.ColumnName, out string? header)
                    && header is not null
                    && string.IsNullOrWhiteSpace(row.GetValueOrDefault(header))));
            }

            HashSet<string> headers = SheetHeaders(sheet);
            List<string> unmappedHeaders = headers.Where(h => !ws.Columns.ContainsKey(h)).OrderBy(h => h, StringComparer.Ordinal).ToList();

            list.Add(new ValidationVm(ws.Worksheet, ws.Table, sheet.Rows.Count, sheet.Rows.Count - wouldSkip, wouldSkip, missingRequired, unmappedHeaders));
        }

        return list;
    }

    public WorksheetReport? ReportFor(string worksheet) => Report?.Worksheets.FirstOrDefault(w => w.Worksheet == worksheet);

    private static HashSet<string> SheetHeaders(WorksheetData sheet)
    {
        HashSet<string> headers = new(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string?> row in sheet.Rows)
        {
            foreach (string key in row.Keys)
            {
                headers.Add(key);
            }
        }

        return headers;
    }

    public string FmtInt(int n) => n.ToString("N0", _lang.IsFrench ? System.Globalization.CultureInfo.GetCultureInfo("fr-FR") : System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    public static string FmtSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };

    // ---------------- i18n (feature strings) ----------------
    private static readonly Dictionary<string, (string En, string Fr)> Tr = new(StringComparer.Ordinal)
    {
        ["appTitle"] = ("Data Import", "Importation de données"),
        ["envBadge"] = ("Local database", "Base locale"),
        ["target"] = ("Target", "Cible"),
        ["targetDb"] = ("Local working copy", "Copie de travail locale"),
        ["stepSource"] = ("Source", "Source"), ["stepMapping"] = ("Mapping", "Correspondance"),
        ["stepValidate"] = ("Validate", "Validation"), ["stepLoad"] = ("Load", "Chargement"), ["stepRecap"] = ("Recap", "Récapitulatif"),
        ["kSource"] = ("Step 1", "Étape 1"), ["kMapping"] = ("Step 2", "Étape 2"), ["kValidate"] = ("Step 3", "Étape 3"),
        ["kLoad"] = ("Step 4", "Étape 4"), ["kRecap"] = ("Step 5", "Étape 5"),
        ["back"] = ("Back", "Retour"), ["next"] = ("Next", "Suivant"), ["finish"] = ("Finish", "Terminer"),
        ["runValidation"] = ("Validate", "Valider"), ["goLoad"] = ("Continue to load", "Passer au chargement"), ["viewRecap"] = ("View recap →", "Voir le récapitulatif →"),
        ["s1Title"] = ("Source workbook", "Classeur source"),
        ["s1Sub"] = ("Upload the team's Excel workbook. We read each worksheet that the mapping recognizes — no external tools.", "Téléversez le classeur Excel de l'équipe. Nous lisons chaque feuille reconnue par la correspondance — sans outil externe."),
        ["dropTitle"] = ("Drop your .xlsx here", "Déposez votre .xlsx ici"), ["dropOr"] = ("or", "ou"),
        ["dropBrowse"] = ("browse to upload", "parcourir pour téléverser"), ["accepted"] = (".xlsx workbook", "classeur .xlsx"),
        ["reading"] = ("Reading workbook…", "Lecture du classeur…"), ["parsed"] = ("Parsed", "Analysé"),
        ["worksheets"] = ("Worksheets", "Feuilles"), ["rows"] = ("rows", "lignes"), ["cols"] = ("cols", "col."),
        ["mappedTo"] = ("→", "→"), ["preview"] = ("Live preview", "Aperçu"),
        ["unmappedCols"] = ("ignored column(s)", "colonne(s) ignorée(s)"),
        ["s2Title"] = ("Column mapping", "Correspondance des colonnes"),
        ["s2Sub"] = ("Each worksheet maps to a target table. Listed columns import; everything else is ignored (derived/computed columns).", "Chaque feuille correspond à une table cible. Les colonnes listées sont importées ; le reste est ignoré (colonnes dérivées/calculées)."),
        ["source"] = ("Source column", "Colonne source"), ["targetCol"] = ("Target column", "Colonne cible"), ["ignore"] = ("— ignored —", "— ignorée —"),
        ["required"] = ("required", "requis"), ["reference"] = ("reference", "référence"), ["expression"] = ("expression", "expression"),
        ["s3Title"] = ("Validate (dry run)", "Validation (test à blanc)"),
        ["s3Sub"] = ("We check the workbook against the target schema. Nothing is written yet.", "Nous vérifions le classeur selon le schéma cible. Rien n'est encore écrit."),
        ["willImport"] = ("will import", "à importer"), ["willSkip"] = ("will skip", "à ignorer"),
        ["missingReq"] = ("required column not mapped:", "colonne requise non associée :"),
        ["skipNote"] = ("rows missing a required value are skipped and reported", "les lignes sans valeur requise sont ignorées et signalées"),
        ["allValid"] = ("All rows pass the required-field checks.", "Toutes les lignes passent les contrôles de champs requis."),
        ["s4Title"] = ("Load into the local database", "Charger dans la base locale"),
        ["s4Sub"] = ("Run the import. Rows are upserted by natural key as one reviewable change set — re-running updates instead of duplicating.", "Lancez l'import. Les lignes sont fusionnées par clé naturelle en un lot révisable — relancer met à jour au lieu de dupliquer."),
        ["confirmLoad"] = ("Import into local database", "Importer dans la base locale"),
        ["loading"] = ("Importing…", "Importation…"), ["willHappen"] = ("What will happen", "Ce qui va se passer"),
        ["cancel"] = ("Cancel", "Annuler"), ["importCancelled"] = ("Import cancelled", "Import annulé"),
        ["cancelledNote"] = ("Rows imported before cancelling were kept (re-running completes the rest).", "Les lignes importées avant l'annulation ont été conservées (relancer termine le reste)."),
        ["rowsImported"] = ("rows imported", "lignes importées"),
        ["fileTooLarge"] = ("File is too large (max 50 MB).", "Fichier trop volumineux (max 50 Mo)."),
        ["tooManyRows"] = ("Workbook has too many rows (max 100,000).", "Le classeur contient trop de lignes (max 100 000)."),
        ["rowsToLoad"] = ("rows to import", "lignes à importer"), ["intoTables"] = ("target tables", "tables cibles"),
        ["mappingName"] = ("Mapping", "Correspondance"), ["changeSet"] = ("Change set", "Lot de modifications"),
        ["s5Title"] = ("Import complete", "Import terminé"),
        ["s5Sub"] = ("Rows were written to your local working copy. Review them in the editors, then Publish to share.", "Les lignes ont été écrites dans votre copie locale. Vérifiez-les dans les éditeurs, puis Publiez pour partager."),
        ["created"] = ("Created", "Créées"), ["updated"] = ("Updated", "Mises à jour"), ["skipped"] = ("Skipped", "Ignorées"),
        ["resolved"] = ("Refs resolved", "Réf. résolues"), ["unresolved"] = ("Refs unresolved", "Réf. non résolues"),
        ["perTable"] = ("Per worksheet", "Par feuille"), ["table"] = ("Table", "Table"),
        ["unmapped"] = ("Ignored columns", "Colonnes ignorées"), ["errors"] = ("Skipped rows", "Lignes ignorées"),
        ["startNew"] = ("Import another workbook", "Importer un autre classeur"),
        ["nothingLoaded"] = ("Nothing to import — upload a workbook first.", "Rien à importer — téléversez d'abord un classeur."),
        ["theme"] = ("Theme", "Thème"),
    };

    public string T(string key) => Tr.TryGetValue(key, out (string En, string Fr) s) ? (IsFrench ? s.Fr : s.En) : key;
}
