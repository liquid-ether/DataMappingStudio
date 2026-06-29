namespace App.Importer;

/// <summary>
/// Importer configuration (bound from <c>appsettings.json</c> section <c>Importer</c>, overridable by
/// <c>DMS_Importer__*</c> environment variables and by explicit command-line arguments). The defaults
/// target the same local working copy the desktop/web app uses
/// (<c>%LOCALAPPDATA%\MappingStudio\local.db</c>), so the importer "just works" against the app's
/// database with no arguments. Paths may contain environment variables (e.g. <c>%LOCALAPPDATA%</c>).
/// </summary>
public sealed class ImporterConfig
{
    public string? DbPath { get; set; }
    public string? MappingPath { get; set; }
    public string? RemoteFolder { get; set; }
    public string ChangedBy { get; set; } = "import";

    private static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MappingStudio");

    /// <summary>The local SQLite working copy to import into (CLI arg wins, then config, then the app default).</summary>
    public string ResolveDbPath(string? arg)
        => Expand(arg ?? DbPath ?? Path.Combine(AppDataDir, "local.db"));

    /// <summary>
    /// The worksheet→table mapping. An explicit command-line argument is taken relative to the caller's
    /// working directory; the configured/default mapping resolves next to the importer executable (where
    /// the bundled <c>import-mapping.json</c> ships), so the importer is self-contained with no argument.
    /// </summary>
    public string ResolveMappingPath(string? arg)
    {
        if (arg is not null)
        {
            return Expand(arg);
        }

        string path = Expand(MappingPath ?? "import-mapping.json");
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    /// <summary>The per-writer remote folder (needed to construct the store graph; not written by import).</summary>
    public string ResolveRemoteFolder(string? arg, string dbPath)
        => Expand(arg ?? RemoteFolder ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "remote"));

    private static string Expand(string value) => Environment.ExpandEnvironmentVariables(value);
}
