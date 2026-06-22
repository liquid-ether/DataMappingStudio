using App.Application.Provisioning;
using App.Domain.Catalog;
using App.Domain.Entities;

namespace App.Application.Tests.Provisioning;

public class DefaultCatalogTests
{
    private static readonly IReadOnlyList<ColumnCatalogEntry> Entries = DefaultCatalog.Entries();

    [Theory]
    [InlineData(TableNames.Application)]
    [InlineData(TableNames.DataSource)]
    [InlineData(TableNames.DictionaryEntry)]
    [InlineData(TableNames.LookupValue)]
    [InlineData(TableNames.Classification)]
    [InlineData(TableNames.Rule)]
    [InlineData(TableNames.Mapping)]
    public void Every_core_entity_has_columns(string table)
    {
        Assert.NotEmpty(Entries.Where(e => e.TableName == table));
    }

    [Theory]
    [InlineData(TableNames.Application, "app_code")]
    [InlineData(TableNames.DataSource, "name")]
    [InlineData(TableNames.DictionaryEntry, "column_name")]
    [InlineData(TableNames.LookupValue, "lookup_type")]
    [InlineData(TableNames.Classification, "prp")]
    [InlineData(TableNames.Rule, "name")]
    public void Natural_key_columns_are_required(string table, string column)
    {
        ColumnCatalogEntry entry = Entries.Single(e => e.TableName == table && e.ColumnName == column);

        Assert.True(entry.IsRequired);
    }

    [Fact]
    public void All_entries_are_valid_and_bilingual()
    {
        Assert.All(Entries, e =>
        {
            Assert.True(e.IsValid);
            Assert.False(string.IsNullOrWhiteSpace(e.LabelEn));
            Assert.False(string.IsNullOrWhiteSpace(e.LabelFr));
        });
    }

    [Fact]
    public void Navigation_lists_the_editable_entities()
    {
        Assert.Equal(
            [TableNames.Application, TableNames.DataSource, TableNames.DictionaryEntry, TableNames.LookupValue, TableNames.Classification, TableNames.Rule],
            DefaultCatalog.Navigation.Select(n => n.Table));
    }
}
