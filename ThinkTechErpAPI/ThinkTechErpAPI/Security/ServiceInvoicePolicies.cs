namespace ThinkTechErpAPI.Security;

public static class ServiceInvoicePolicies
{
    /// <summary>SERVICE_INVOICE.V or SERVICE_INVOICE.M: read service invoices.</summary>
    public const string View = "ServiceInvoiceView";

    /// <summary>SERVICE_INVOICE.M: create, edit and delete service invoices.</summary>
    public const string Manage = "ServiceInvoiceManage";
}
