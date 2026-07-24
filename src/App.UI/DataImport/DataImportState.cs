using App.Application.Abstractions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Application.Security;
using App.Application.Sync;
using App.Domain.Catalog;
using App.Domain.Data;
using App.UI.Localization;
using Microsoft.Extensions.Logging;

namespace App.UI.DataImport;

/// <summary>A worksheet's import summary for the source/mapping steps.</summary>
public sealed record WorksheetVm(string Name, string Table, bool Present, int Rows, int MappedColumns, int SourceColumns, IReadOnlyList<string> UnmappedHeaders);

/// <summary>
/// The editable mapping for one uploaded worksheet: which table it feeds (empty = not imported), which
/// header goes to which column, and the natural-key columns rows upsert by. The wizard's Mapping step
/// edits these; the effective <see cref="ImportMapping"/> is built from them.
/// </summary>
public sealed class SheetMappingDraft
{
    public required string Sheet { get; init; }

    public required IReadOnlyList<string> Headers { get; init; }

    public int RowCount { get; init; }

    /// <summary>Target table, or empty when this worksheet is not imported.</summary>
    public string Table { get; set; } = "";

    /// <summary>Source header → target column (headers absent from the map are ignored).</summary>
    public Dictionary<string, string> HeaderToColumn { get; } = new(StringComparer.Ordinal);

    /// <summary>Columns rows upsert by (must all be mapped).</summary>
    public HashSet<string> NaturalKey { get; } = new(StringComparer.Ordinal);

    /// <summary>FK-by-natural-key resolutions, carried over when a saved mapping declares them.</summary>
    public IReadOnlyDictionary<string, ColumnReference> References { get; set; } = new Dictionary<string, ColumnReference>();

    /// <summary>The expression-bearing column, carried over from a saved mapping.</summary>
    public string? ExpressionColumn { get; set; }

    public bool IsMapped => Table.Length > 0 && HeaderToColumn.Count > 0;
}

/// <summary>A worksheet's dry-run validation outcome (computed without writing anything).</summary>
public sealed record ValidationVm(string Worksheet, string Table, int Rows, int WouldImport, int WouldSkip, IReadOnlyList<string> MissingRequired, IReadOnlyList<string> UnmappedHeaders);

/// <summary>
/// Orchestrates the in-app Data Import wizard against the <b>real</b> local database: every worksheet of
/// an uploaded workbook is parsed by <see cref="IWorkbookReader"/>, mapped to catalog tables in the
/// editable Mapping step (prefilled from the built-in <see cref="DefaultImportMapping"/> template, or
/// from a mapping saved to the shared <see cref="IImportMappingStore"/>), dry-run validated against the
/// column catalog, then loaded through the shared <see cref="ImportEngine"/> into
/// <see cref="ILocalStore"/> as one reviewable Import change set. The same engine and mapping store the
/// CLI uses — the CLI can only run mappings saved here. Scoped per Blazor circuit.
/// </summary>
public sealed class DataImportState : IDisposable
{
    private readonly LanguageState _lang;
    private readonly ICatalog _catalog;
    private readonly ImportEngine _engine;
    private readonly IWorkbookReader _reader;
    private readonly ICurrentUser _user;
    private readonly IImportMappingStore? _mappings;
    private readonly ITableCatalog? _tables;
    private readonly CatalogSyncService? _catalogSync;
    private readonly ILocalStore? _store;
    private readonly ILogger<DataImportState>? _logger;
    private string? _catalogVersion;

    public DataImportState(LanguageState lang, ICatalog catalog, ImportEngine engine, IWorkbookReader reader, ICurrentUser user, ILogger<DataImportState>? logger = null, IImportMappingStore? mappings = null, ITableCatalog? tables = null, CatalogSyncService? catalogSync = null, ILocalStore? store = null)
    {
        _lang = lang;
        _catalog = catalog;
        _engine = engine;
        _reader = reader;
        _user = user;
        _mappings = mappings;
        _tables = tables;
        _catalogSync = catalogSync;
        _store = store;
        _logger = logger;
        if (_catalogSync is not null)
        {
            _catalogSync.Changed += OnCatalogPublished;
        }
    }

    public event Action? OnChange;

    /// <summary>
    /// Raised when the shared meta-model changed (a column/table was published, possibly from another
    /// circuit). The wizard handles it on its own dispatcher via <see cref="RefreshFromCatalog"/> —
    /// state is never mutated on the publisher's thread.
    /// </summary>
    public event Action? CatalogModelChanged;

    private void OnCatalogPublished() => CatalogModelChanged?.Invoke();

    public void Dispose()
    {
        if (_catalogSync is not null)
        {
            _catalogSync.Changed -= OnCatalogPublished;
        }
    }

    /// <summary>
    /// Pulls the latest shared meta-model into this working copy (version-gated, so a no-op when
    /// current) and auto-maps still-unmapped headers against any new columns. Call on the circuit's
    /// dispatcher.
    /// </summary>
    public void RefreshFromCatalog()
    {
        EnsureCatalogCurrent();
        foreach (SheetMappingDraft draft in Drafts.Where(d => d.Table.Length > 0))
        {
            AutoMapNewColumns(draft, notify: false);
        }

        Notify();
    }

    private void EnsureCatalogCurrent()
    {
        if (_catalogSync is null || _tables is null || _store is null)
        {
            return;
        }

        try
        {
            string version = _catalogSync.Version();
            if (version == _catalogVersion)
            {
                return;
            }

            _catalogSync.ApplyTo(_catalog, _tables, _store);
            _catalogVersion = version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "Could not refresh the meta-model from the shared folder; the wizard keeps the local copy.");
        }
    }

    private void Notify() => OnChange?.Invoke();

    public bool IsFrench => _lang.IsFrench;

    /// <summary>
    /// The effective worksheet→table mapping, built from the editable per-sheet drafts. Starts prefilled
    /// from the built-in team-workbook template; the Mapping step edits it, and it can be loaded from /
    /// saved to the shared mapping store — the same store the CLI importer consumes.
    /// </summary>
    public ImportMapping Mapping => new()
    {
        Worksheets = Drafts
            .Where(d => d.IsMapped)
            .Select(d => new WorksheetMapping
            {
                Worksheet = d.Sheet,
                Table = d.Table,
                NaturalKey = [.. d.NaturalKey.Where(k => d.HeaderToColumn.ContainsValue(k))],
                Columns = new Dictionary<string, string>(d.HeaderToColumn, StringComparer.Ordinal),
                References = d.References,
                ExpressionColumn = d.ExpressionColumn,
            })
            .ToList(),
    };

    /// <summary>The editable per-sheet mappings (one per worksheet found in the uploaded file).</summary>
    public IReadOnlyList<SheetMappingDraft> Drafts { get; private set; } = [];

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

    /// <summary>Rows that will actually be imported (mapped worksheets only).</summary>
    public int TotalRows => Drafts.Where(d => d.IsMapped).Sum(d => d.RowCount);

    public IReadOnlyList<string> TargetTables => Mapping.Worksheets.Select(w => w.Table).Distinct().ToList();

    // ---------------- mapping editor ----------------

    /// <summary>Tables a worksheet may target (every catalog table, meta tables excluded by the catalog).</summary>
    public IReadOnlyList<string> AvailableTables => _catalog.GetTables();

    /// <summary>Mappable columns of a table (no core/computed columns — computed values are derived).</summary>
    public IReadOnlyList<ColumnCatalogEntry> MappableColumns(string table)
        => TableColumns(table).Where(c => !c.IsCore && c.Kind != ColumnKind.Computed).ToList();

    /// <summary>Selects (or clears) a worksheet's target table and auto-maps headers to columns by name.</summary>
    public void SetDraftTable(SheetMappingDraft draft, string table)
    {
        draft.Table = table;
        draft.HeaderToColumn.Clear();
        draft.NaturalKey.Clear();
        draft.References = new Dictionary<string, ColumnReference>();
        draft.ExpressionColumn = null;
        if (table.Length == 0)
        {
            Notify();
            return;
        }

        // Auto-map: header matches column name or a bilingual label (case/space/underscore-insensitive).
        IReadOnlyList<ColumnCatalogEntry> columns = MappableColumns(table);
        MapUnmappedHeaders(draft, columns);

        // Natural-key default: the mapped required columns, else the first mapped column.
        foreach (ColumnCatalogEntry required in columns.Where(c => c.IsRequired && draft.HeaderToColumn.ContainsValue(c.ColumnName)))
        {
            draft.NaturalKey.Add(required.ColumnName);
        }

        if (draft.NaturalKey.Count == 0 && draft.HeaderToColumn.Count > 0)
        {
            draft.NaturalKey.Add(draft.HeaderToColumn.Values.First());
        }

        Notify();
    }

    /// <summary>
    /// Auto-maps still-unmapped headers to still-unused columns (manual choices are never clobbered).
    /// The explicit "auto-map" action for columns added after the file was uploaded.
    /// </summary>
    public void AutoMapNewColumns(SheetMappingDraft draft, bool notify = true)
    {
        if (draft.Table.Length == 0)
        {
            return;
        }

        MapUnmappedHeaders(draft, MappableColumns(draft.Table));
        if (draft.NaturalKey.Count == 0 && draft.HeaderToColumn.Count > 0)
        {
            draft.NaturalKey.Add(draft.HeaderToColumn.Values.First());
        }

        if (notify)
        {
            Notify();
        }
    }

    private static void MapUnmappedHeaders(SheetMappingDraft draft, IReadOnlyList<ColumnCatalogEntry> columns)
    {
        foreach (string header in draft.Headers.Where(h => !draft.HeaderToColumn.ContainsKey(h)))
        {
            ColumnCatalogEntry? match = columns.FirstOrDefault(c =>
                Normalized(header) == Normalized(c.ColumnName)
                || Normalized(header) == Normalized(c.LabelEn ?? "")
                || Normalized(header) == Normalized(c.LabelFr ?? ""));
            if (match is not null && !draft.HeaderToColumn.ContainsValue(match.ColumnName))
            {
                draft.HeaderToColumn[header] = match.ColumnName;
            }
        }
    }

    /// <summary>Dropdown text for a mappable column: bilingual label, column name, required marker.</summary>
    public string ColumnOptionLabel(ColumnCatalogEntry c)
    {
        string label = _lang.Label(c.LabelEn, c.LabelFr);
        string display = label.Length == 0 || string.Equals(label, c.ColumnName, StringComparison.Ordinal)
            ? c.ColumnName
            : $"{label} ({c.ColumnName})";
        return c.IsRequired ? display + " *" : display;
    }

    /// <summary>Maps (or unmaps, with an empty column) one source header.</summary>
    public void SetHeaderColumn(SheetMappingDraft draft, string header, string column)
    {
        string? previous = draft.HeaderToColumn.GetValueOrDefault(header);
        if (column.Length == 0)
        {
            draft.HeaderToColumn.Remove(header);
        }
        else
        {
            // A column can only be fed by one header: steal it from any other header.
            foreach (string other in draft.HeaderToColumn.Where(kv => kv.Value == column).Select(kv => kv.Key).ToList())
            {
                draft.HeaderToColumn.Remove(other);
            }

            draft.HeaderToColumn[header] = column;
        }

        if (previous is not null && !draft.HeaderToColumn.ContainsValue(previous))
        {
            draft.NaturalKey.Remove(previous);
        }

        Notify();
    }

    public void ToggleNaturalKey(SheetMappingDraft draft, string column)
    {
        if (!draft.NaturalKey.Remove(column))
        {
            draft.NaturalKey.Add(column);
        }

        Notify();
    }

    /// <summary>Human-readable problems that must be fixed before validating/loading.</summary>
    public IReadOnlyList<string> MappingErrors()
    {
        List<string> errors = [];
        if (!Drafts.Any(d => d.IsMapped))
        {
            errors.Add(IsFrench ? "Aucune feuille n'est associée à une table." : "No worksheet is mapped to a table.");
        }

        foreach (SheetMappingDraft draft in Drafts.Where(d => d.Table.Length > 0))
        {
            // A saved mapping can reference tables/columns the current model no longer has (or that
            // haven't synced here yet) — surface that instead of failing at import time.
            if (!AvailableTables.Contains(draft.Table, StringComparer.Ordinal))
            {
                errors.Add(IsFrench
                    ? $"« {draft.Sheet} » : la table « {draft.Table} » n'existe pas dans le modèle actuel."
                    : $"'{draft.Sheet}': table '{draft.Table}' does not exist in the current model.");
                continue;
            }

            HashSet<string> known = MappableColumns(draft.Table).Select(c => c.ColumnName).ToHashSet(StringComparer.Ordinal);
            List<string> unknown = draft.HeaderToColumn.Values.Where(c => !known.Contains(c)).Distinct().ToList();
            if (unknown.Count > 0)
            {
                errors.Add(IsFrench
                    ? $"« {draft.Sheet} » : colonne(s) inconnue(s) dans « {draft.Table} » : {string.Join(", ", unknown)}."
                    : $"'{draft.Sheet}': column(s) not in '{draft.Table}': {string.Join(", ", unknown)}.");
            }

            if (draft.HeaderToColumn.Count == 0)
            {
                errors.Add(IsFrench ? $"« {draft.Sheet} » : aucune colonne associée." : $"'{draft.Sheet}': no columns mapped.");
                continue;
            }

            if (draft.NaturalKey.Count == 0 || !draft.NaturalKey.All(k => draft.HeaderToColumn.ContainsValue(k)))
            {
                errors.Add(IsFrench
                    ? $"« {draft.Sheet} » : choisissez au moins une colonne clé (associée) pour l'upsert."
                    : $"'{draft.Sheet}': pick at least one (mapped) key column for the upsert.");
            }
        }

        return errors;
    }

    /// <summary>Prefills the drafts from a mapping (sheet names matched case-insensitively).</summary>
    public void ApplyTemplate(ImportMapping template)
    {
        foreach (SheetMappingDraft draft in Drafts)
        {
            WorksheetMapping? ws = template.Worksheets.FirstOrDefault(w => string.Equals(w.Worksheet, draft.Sheet, StringComparison.OrdinalIgnoreCase));
            if (ws is null)
            {
                continue;
            }

            draft.Table = ws.Table;
            draft.HeaderToColumn.Clear();
            foreach ((string header, string column) in ws.Columns)
            {
                if (draft.Headers.Contains(header, StringComparer.Ordinal))
                {
                    draft.HeaderToColumn[header] = column;
                }
            }

            draft.NaturalKey.Clear();
            foreach (string key in ws.NaturalKey)
            {
                draft.NaturalKey.Add(key);
            }

            draft.References = ws.References;
            draft.ExpressionColumn = ws.ExpressionColumn;

            // Headers the template doesn't know (e.g. a column added to the model after the template
            // was authored) still auto-map against the live catalog.
            MapUnmappedHeaders(draft, MappableColumns(draft.Table));
        }

        Notify();
    }

    // ---------------- saved mappings (shared with the CLI importer) ----------------

    public bool MappingStoreAvailable => _mappings is not null;

    private IReadOnlyList<SavedMappingInfo> _savedMappings = [];

    /// <summary>The cached saved-mapping list (the toolbar renders this every frame — no file IO here).</summary>
    public IReadOnlyList<SavedMappingInfo> SavedMappings() => _savedMappings;

    /// <summary>Re-reads the saved-mapping list from the shared folder (upload, step entry, save/delete).</summary>
    private void RefreshSavedMappings()
    {
        try
        {
            _savedMappings = _mappings?.List() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _savedMappings = [];
        }
    }

    /// <summary>Deletes a saved mapping from the shared store (rename = save under a new name + delete).</summary>
    public void DeleteMapping(string name)
    {
        if (_mappings is null)
        {
            MappingMessage = IsFrench ? "Aucun dossier partagé configuré." : "No shared folder is configured.";
            Notify();
            return;
        }

        try
        {
            MappingMessage = _mappings.Delete(name)
                ? (IsFrench ? $"Association « {name} » supprimée." : $"Mapping '{name}' deleted.")
                : (IsFrench ? $"Association « {name} » introuvable." : $"Mapping '{name}' not found.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MappingMessage = ex.Message;
        }

        RefreshSavedMappings();
        Notify();
    }

    public string? MappingMessage { get; private set; }

    public void LoadBuiltInMapping()
    {
        ApplyTemplate(DefaultImportMapping.Mapping);
        MappingMessage = IsFrench ? "Modèle intégré appliqué." : "Built-in template applied.";
        Notify();
    }

    public void LoadSavedMapping(string name)
    {
        try
        {
            ImportMapping? saved = _mappings?.Get(name);
            MappingMessage = saved is null
                ? (IsFrench ? $"Association « {name} » introuvable." : $"Mapping '{name}' not found.")
                : (IsFrench ? $"Association « {name} » appliquée." : $"Mapping '{name}' applied.");
            if (saved is not null)
            {
                ApplyTemplate(saved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            MappingMessage = ex.Message;
        }

        Notify();
    }

    /// <summary>Saves the current effective mapping under a name — this is what the CLI can then run.</summary>
    public void SaveMapping(string name)
    {
        if (_mappings is null)
        {
            MappingMessage = IsFrench ? "Aucun dossier partagé configuré." : "No shared folder is configured.";
            Notify();
            return;
        }

        try
        {
            IReadOnlyList<string> errors = MappingErrors();
            if (errors.Count > 0)
            {
                MappingMessage = errors[0];
            }
            else
            {
                _mappings.Save(name, Mapping, _user.Name);
                MappingMessage = IsFrench
                    ? $"Association « {name.Trim()} » enregistrée — utilisable par l'outil ligne de commande."
                    : $"Mapping '{name.Trim()}' saved — usable from the command-line importer.";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MappingMessage = ex.Message;
        }

        RefreshSavedMappings();
        Notify();
    }

    private static string Normalized(string value)
        => new([.. value.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').Select(char.ToLowerInvariant)]);

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
            // Make sure the drafts (and their template prefill) see the current shared meta-model.
            EnsureCatalogCurrent();
            RefreshSavedMappings();

            // Copy to a seekable stream (InputFile streams are forward-only) and parse.
            using MemoryStream buffer = new();
            await content.CopyToAsync(buffer);
            buffer.Position = 0;

            IReadOnlyList<WorksheetData> parsed = _reader.ReadAll(buffer);
            if (parsed.All(s => s.Rows.Count == 0))
            {
                ParseError = IsFrench
                    ? "Le classeur ne contient aucune ligne de données."
                    : "The workbook contains no data rows.";
                Parsed = null;
                Drafts = [];
            }
            else if (parsed.Where(s => s.Rows.Count > 0).Sum(s => s.Rows.Count) > MaxRows)
            {
                ParseError = T("tooManyRows");
                Parsed = null;
                Drafts = [];
            }
            else
            {
                Parsed = parsed;
                FileName = name;
                FileSize = size;

                // One editable draft per populated worksheet, prefilled from the built-in team-workbook
                // template so a standard workbook flows through unchanged.
                Drafts = parsed
                    .Where(s => s.Rows.Count > 0)
                    .Select(s => new SheetMappingDraft
                    {
                        Sheet = s.Worksheet,
                        Headers = OrderedHeaders(s),
                        RowCount = s.Rows.Count,
                    })
                    .ToList();
                MappingMessage = null;
                ApplyTemplate(DefaultImportMapping.Mapping);
            }
        }
        catch (Exception ex)
        {
            ParseError = (IsFrench ? "Échec de lecture du classeur : " : "Could not read the workbook: ") + ex.Message;
            Parsed = null;
            Drafts = [];
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
        Drafts = []; MappingMessage = null;
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
            if (Step == 1) { EnterMappingStep(); }
            Notify();
        }
    }

    /// <summary>Whether the Mapping step lets the user continue (something mapped, nothing broken).</summary>
    public bool MappingReady => Drafts.Any(d => d.IsMapped) && MappingErrors().Count == 0;

    public void GoNext()
    {
        if (Step >= StepCount - 1) { return; }
        if (Step == 0 && !HasFile) { return; }
        if (Step == 1 && !MappingReady) { return; }
        Step++;
        MaxStep = Math.Max(MaxStep, Step);
        if (Step == 1) { EnterMappingStep(); }
        Notify();
    }

    /// <summary>The Mapping step always opens against the current shared model and mapping list.</summary>
    private void EnterMappingStep()
    {
        EnsureCatalogCurrent();
        RefreshSavedMappings();
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
    /// <summary>Whether the current user may run an import (gates the Load step; guest admin is always true).</summary>
    public bool CanImport => _user.HasPermission(Permissions.DataImport);

    public async Task RunImportAsync()
    {
        if (Parsed is null || Loading) { return; }

        if (!CanImport)
        {
            LoadError = _lang.IsFrench ? "Vous n'avez pas la permission d'importer." : "You don't have permission to import.";
            Notify();
            return;
        }

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

    public WorksheetData? SheetFor(string worksheet) => Parsed?.FirstOrDefault(s => s.Worksheet == worksheet);

    /// <summary>Summary card per worksheet for the Source/Mapping steps (every populated sheet, mapped or not).</summary>
    public IReadOnlyList<WorksheetVm> Worksheets() => Drafts
        .Select(d => new WorksheetVm(
            d.Sheet,
            d.Table,
            true,
            d.RowCount,
            d.HeaderToColumn.Count,
            d.Headers.Count,
            d.Headers.Where(h => !d.HeaderToColumn.ContainsKey(h)).OrderBy(h => h, StringComparer.Ordinal).ToList()))
        .ToList();

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

    /// <summary>Headers in worksheet column order (the reader emits every header on every row).</summary>
    private static List<string> OrderedHeaders(WorksheetData sheet)
    {
        List<string> ordered = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string?> row in sheet.Rows)
        {
            foreach (string key in row.Keys)
            {
                if (seen.Add(key))
                {
                    ordered.Add(key);
                }
            }
        }

        return ordered;
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
        ["s2Sub"] = ("Map each worksheet to a table and its columns, and pick the key column(s) rows upsert by. Save the mapping to reuse it — the command-line importer runs saved mappings.", "Associez chaque feuille à une table et ses colonnes, puis choisissez la ou les colonnes clés pour la fusion. Enregistrez la correspondance pour la réutiliser — l'outil ligne de commande exécute les correspondances enregistrées."),
        ["source"] = ("Source column", "Colonne source"), ["targetCol"] = ("Target column", "Colonne cible"), ["ignore"] = ("— ignored —", "— ignorée —"),
        ["templates"] = ("Mapping", "Correspondance"), ["builtin"] = ("Built-in template", "Modèle intégré"),
        ["savedMappings"] = ("Saved mappings…", "Correspondances enregistrées…"), ["apply"] = ("Apply", "Appliquer"),
        ["saveAs"] = ("Save mapping as…", "Enregistrer sous…"), ["save"] = ("Save", "Enregistrer"),
        ["notImported"] = ("not imported", "non importée"), ["targetTable"] = ("Target table", "Table cible"),
        ["key"] = ("key", "clé"),
        ["keyHint"] = ("Key columns identify existing rows (re-running updates instead of duplicating).", "Les colonnes clés identifient les lignes existantes (relancer met à jour au lieu de dupliquer)."),
        ["pickTableNote"] = ("Pick a target table to import this worksheet, or leave it unmapped to skip it.", "Choisissez une table cible pour importer cette feuille, ou laissez-la sans correspondance pour l'ignorer."),
        ["autoMap"] = ("Auto-map new columns", "Associer les nouvelles colonnes"),
        ["deleteMapping"] = ("Delete", "Supprimer"),
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
        ["warnings"] = ("Warnings", "Avertissements"), ["rejections"] = ("Rejections & warnings", "Rejets et avertissements"), ["more"] = ("more", "de plus"),
        ["resolved"] = ("Refs resolved", "Réf. résolues"), ["unresolved"] = ("Refs unresolved", "Réf. non résolues"),
        ["perTable"] = ("Per worksheet", "Par feuille"), ["table"] = ("Table", "Table"),
        ["unmapped"] = ("Ignored columns", "Colonnes ignorées"), ["errors"] = ("Skipped rows", "Lignes ignorées"),
        ["startNew"] = ("Import another workbook", "Importer un autre classeur"),
        ["nothingLoaded"] = ("Nothing to import — upload a workbook first.", "Rien à importer — téléversez d'abord un classeur."),
        ["theme"] = ("Theme", "Thème"),
    };

    public string T(string key) => Tr.TryGetValue(key, out (string En, string Fr) s) ? (IsFrench ? s.Fr : s.En) : key;
}
