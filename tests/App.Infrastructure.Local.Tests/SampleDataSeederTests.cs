using App.Application.Catalog;
using App.Application.Provisioning;
using App.Application.References;
using App.Domain.Data;
using App.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local.Tests;

/// <summary>Seeds a real temp-file store with the demo dataset and verifies what landed.</summary>
public sealed class SampleDataSeederTests : IDisposable
{
    private readonly string _dir;
    private readonly LocalDatabase _db;
    private readonly SqliteCatalog _catalog;
    private readonly SqliteLocalStore _store;

    public SampleDataSeederTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dms-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new LocalDatabase($"Data Source={Path.Combine(_dir, "local.db")}");
        _catalog = new SqliteCatalog(_db);
        _catalog.Seed(DefaultCatalog.Entries());
        _store = new SqliteLocalStore(_db, _catalog, new SqliteAuditLog(_db), new FakeClock(DateTimeOffset.UnixEpoch));
        _store.EnsureSchema();
    }

    [Fact]
    public void Seeds_all_tables_with_the_requested_counts()
    {
        int written = new SampleDataSeeder(_db).SeedIfEmpty();

        Assert.True(written > 5000);
        Assert.Equal(10, _store.GetAll(TableNames.Application).Count);
        Assert.Equal(100, _store.GetAll(TableNames.DataSource).Count);
        Assert.Equal(5000, _store.GetAll(TableNames.DictionaryEntry).Count);
        Assert.Equal(5, _store.GetAll(TableNames.Classification).Count);
        Assert.Equal(30, _store.GetAll(TableNames.MappingTarget).Count);
        Assert.NotEmpty(_store.GetAll(TableNames.MappingSource));
        Assert.NotEmpty(_store.GetAll(TableNames.Mapping));
    }

    [Fact]
    public void Is_idempotent_when_the_store_already_has_data()
    {
        new SampleDataSeeder(_db).SeedIfEmpty();
        int second = new SampleDataSeeder(_db).SeedIfEmpty();

        Assert.Equal(0, second);
        Assert.Equal(100, _store.GetAll(TableNames.DataSource).Count); // unchanged
    }

    [Fact]
    public void Computed_field_count_matches_the_seeded_dictionary_entries()
    {
        new SampleDataSeeder(_db).SeedIfEmpty();
        ReferenceService references = new(_catalog, _store);

        Row source = _store.GetAll(TableNames.DataSource).First();
        int actualFields = _store.GetAll(TableNames.DictionaryEntry).Count(e => e["source_id"] == source.Id.ToString());

        Assert.Equal(actualFields.ToString(), references.Evaluate(TableNames.DataSource, "field_count", source));
        Assert.InRange(actualFields, 5, 150);
    }

    [Fact]
    public void Catalog_query_exposes_sources_and_their_dictionary_fields()
    {
        new SampleDataSeeder(_db).SeedIfEmpty();
        CatalogQuery query = new(_store);

        Assert.Equal(100, query.DataSources().Count);

        DataSourceInfo source = query.DataSources()[0];
        int expected = _store.GetAll(TableNames.DictionaryEntry).Count(e => e["source_id"] == source.Id.ToString());
        Assert.Equal(expected, query.FieldNames(source.Name).Count);
        Assert.Equal(expected, query.FieldsByDataSourceName()[source.Name].Count);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
