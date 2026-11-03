namespace ThinkTechErpAPI.Security;

public static class AccountingPolicies
{
    /// <summary>ACCOUNTING.V, ACCOUNTING.M or ACCOUNTING.P: read the chart of accounts, vouchers and accounting reports.</summary>
    public const string View = "AccountingView";

    /// <summary>ACCOUNTING.M: change the chart of accounts; enter, edit, delete and cancel vouchers; record bill payments and collections.</summary>
    public const string Manage = "AccountingManage";

    /// <summary>ACCOUNTING.P: post draft vouchers to the ledgers and unpost posted ones back to drafts.</summary>
    public const string Post = "AccountingPost";

    /// <summary>
    /// What View allows, or INVENTORY.M or PURCHASE.M: the ledger head picker, which the category and supplier
    /// forms need as well to map a head.
    /// </summary>
    public const string LedgerHeadLookup = "AccountingLedgerHeadLookup";
}
