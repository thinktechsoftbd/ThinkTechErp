namespace ThinkTechErpAPI.Security;

public static class AccountingPolicies
{
    /// <summary>ACCOUNTING.V or ACCOUNTING.M: read the chart of accounts, vouchers and accounting reports.</summary>
    public const string View = "AccountingView";

    /// <summary>ACCOUNTING.M: change the chart of accounts; enter, post and cancel vouchers, bill payments and collections.</summary>
    public const string Manage = "AccountingManage";
}
