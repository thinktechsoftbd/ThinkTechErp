namespace ThinkTechErpAPI.Security;

public static class InventoryPolicies
{
    /// <summary>INVENTORY.V or INVENTORY.M: read products, warehouses, stock documents and reports.</summary>
    public const string View = "InventoryView";

    /// <summary>INVENTORY.M: change inventory settings and enter or post stock documents.</summary>
    public const string Manage = "InventoryManage";

    /// <summary>
    /// Any of INVENTORY.V/M, PURCHASE.V/M or SALES.V/M: the product, warehouse and tax pickers the
    /// purchase and sales screens need as well.
    /// </summary>
    public const string Lookup = "InventoryLookup";

    /// <summary>INVENTORY.M or PURCHASE.M: setup both modules share, such as payment sources.</summary>
    public const string SharedManage = "InventoryOrPurchaseManage";
}

public static class SalesPolicies
{
    /// <summary>SALES.V or SALES.M: read customers, sales invoices, customer receipts and sales reports.</summary>
    public const string View = "SalesView";

    /// <summary>SALES.M: change customers; enter and post sales invoices and customer receipts.</summary>
    public const string Manage = "SalesManage";
}

public static class PurchasePolicies
{
    /// <summary>PURCHASE.V or PURCHASE.M: read suppliers, taxes, purchase documents and reports.</summary>
    public const string View = "PurchaseView";

    /// <summary>PURCHASE.M: change suppliers and taxes; enter and post purchase documents and payments.</summary>
    public const string Manage = "PurchaseManage";
}
