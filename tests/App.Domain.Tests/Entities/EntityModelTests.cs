using App.Domain.Entities;
using App.Domain.Expressions;
using App.Domain.Lineage;

namespace App.Domain.Tests.Entities;

public class EntityModelTests
{
    [Theory]
    [InlineData(MappingKind.Field, true)]
    [InlineData(MappingKind.Calc, true)]
    [InlineData(MappingKind.Join, false)]
    [InlineData(MappingKind.Filter, false)]
    public void Mapping_only_field_and_calc_produce_a_target_field(MappingKind kind, bool produces)
    {
        Mapping mapping = new()
        {
            Kind = kind,
            Expression = RuleExpression.RawText("a.x"),
        };

        Assert.Equal(produces, mapping.ProducesField);
    }

    [Fact]
    public void New_dictionary_entry_is_nullable_by_default()
    {
        DictionaryEntry entry = new() { ColumnName = "col" };

        Assert.True(entry.IsNullable);
    }

    [Fact]
    public void New_sync_metadata_starts_at_zero_and_not_deleted()
    {
        SyncMetadata sync = SyncMetadata.New();

        Assert.Equal(0, sync.RowVersion);
        Assert.Equal(0, sync.BaseVersion);
        Assert.False(sync.IsDeleted);
    }

    [Fact]
    public void Lineage_node_id_matches_the_mockup_key_format()
    {
        Assert.Equal("CUSTOMER_360§risk_band", LineageNode.MakeId("CUSTOMER_360", "risk_band"));
    }

    [Fact]
    public void Domain_tables_are_listed_in_fk_dependency_order()
    {
        IReadOnlyList<string> order = TableNames.DomainTablesInDependencyOrder;

        Assert.True(order.ToList().IndexOf(TableNames.Application) < order.ToList().IndexOf(TableNames.DataSource));
        Assert.True(order.ToList().IndexOf(TableNames.DataSource) < order.ToList().IndexOf(TableNames.DictionaryEntry));
        Assert.True(order.ToList().IndexOf(TableNames.DictionaryEntry) < order.ToList().IndexOf(TableNames.Mapping));
    }
}
