using App.Application.Lineage;
using App.Domain.Entities;

namespace App.Application.Tests;

/// <summary>
/// The mockup's <c>state.targets</c>/<c>state.rows</c> ported verbatim, so engine tests assert against
/// the same known-good expressions and lineage results the approved UI prototype produces.
/// </summary>
public static class MockupScenario
{
    public static readonly IReadOnlyList<string> CrmAccounts = ["acct_id", "first_name", "last_name", "email", "status", "region", "created_at"];
    public static readonly IReadOnlyList<string> Billing = ["account_ref", "mrr", "plan", "balance", "last_payment"];

    public static LineageScenario Build()
    {
        List<LineageTarget> targets =
        [
            new("CUSTOMER_360",
            [
                LineageSource.Table("a", "a", "CRM_ACCOUNTS", CrmAccounts),
                LineageSource.Table("b", "b", "BILLING", Billing),
            ]),
            new("MARKETING_SEGMENTS",
            [
                LineageSource.Target("c", "tsrc", "CUSTOMER_360"),
            ]),
        ];

        List<LineageRow> rows =
        [
            new(MappingKind.Join, "CUSTOMER_360", "", "a.acct_id = b.account_ref"),
            new(MappingKind.Filter, "CUSTOMER_360", "", "a.status IN ('ACTIVE','PENDING')"),
            new(MappingKind.Field, "CUSTOMER_360", "customer_id", "a.acct_id"),
            new(MappingKind.Field, "CUSTOMER_360", "full_name", "CONCAT(a.first_name,' ',a.last_name)"),
            new(MappingKind.Field, "CUSTOMER_360", "email", "a.email"),
            new(MappingKind.Field, "CUSTOMER_360", "region", "a.region"),
            new(MappingKind.Field, "CUSTOMER_360", "lifetime_value", "COALESCE(b.mrr,0) * 12"),
            new(MappingKind.Field, "CUSTOMER_360", "plan", "b.plan"),
            new(MappingKind.Field, "CUSTOMER_360", "plan_label", "b.plan_label"), // unresolved: BILLING has no plan_label
            new(MappingKind.Calc, "CUSTOMER_360", "risk_band", "CASE WHEN lifetime_value >= 5000 THEN 'HIGH' WHEN lifetime_value >= 1000 THEN 'MED' ELSE 'LOW' END"),
            new(MappingKind.Calc, "CUSTOMER_360", "is_high_value", "IF(lifetime_value >= 1200, 1, 0)"),
            new(MappingKind.Filter, "MARKETING_SEGMENTS", "", "c.is_high_value = 1"),
            new(MappingKind.Field, "MARKETING_SEGMENTS", "base_risk", "c.risk_band"),
            new(MappingKind.Field, "MARKETING_SEGMENTS", "segment_code", "UPPER(c.plan)"),
            new(MappingKind.Calc, "MARKETING_SEGMENTS", "priority", "IF(c.risk_band = 'HIGH' AND c.is_high_value = 1, 'P1', 'P2')"),
        ];

        return new LineageScenario(targets, rows);
    }
}
