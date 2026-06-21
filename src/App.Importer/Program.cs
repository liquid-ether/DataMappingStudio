using App.Importer;
using Microsoft.Extensions.Hosting;

// Excel → SQLite importer entry point. The full pipeline (ImportExcel-exported worksheets →
// validate → transform → FK/expression resolution → load as a reviewable Import change set) is
// implemented in Phase 9. For now this bootstraps the shared host so the composition is exercised.
using IHost host = ImporterHost.Build(args);

Console.WriteLine("App.Importer host ready. Import pipeline arrives in Phase 9.");
return 0;
