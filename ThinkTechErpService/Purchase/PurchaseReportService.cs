using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Purchase;
using ThinkTechErpService.Common;
using ThinkTechErpService.Inventory;

namespace ThinkTechErpService.Purchase;

public interface IPurchaseReportService
{
    Task<SupplierLedger?> GetSupplierLedgerAsync(int organizationId, int supplierId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<ReportPage<SupplierDueRow>> GetSupplierDuesAsync(int organizationId, PurchaseReportQuery query, DateOnly? asOf, CancellationToken cancellationToken = default);
    Task<ReportPage<PurchaseRegisterRow>> GetRegisterAsync(int organizationId, PurchaseReportQuery query, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<ReportPage<PurchaseByProductRow>> GetByProductAsync(int organizationId, PurchaseReportQuery query, DateOnly? from, DateOnly? to, int? categoryId, CancellationToken cancellationToken = default);
}

public sealed class PurchaseReportService(IPurchaseReportRepository reports, IInventorySetupRepository inventorySetup) : IPurchaseReportService
{
    public async Task<SupplierLedger?> GetSupplierLedgerAsync(int organizationId, int supplierId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (start, end) = InventoryReportService.Period(from, to);
        return await reports.GetSupplierLedgerAsync(organizationId, supplierId, start, end, cancellationToken);
    }

    public Task<ReportPage<SupplierDueRow>> GetSupplierDuesAsync(int organizationId, PurchaseReportQuery query, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return reports.GetSupplierDuesAsync(organizationId, query, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    }

    public Task<ReportPage<PurchaseRegisterRow>> GetRegisterAsync(int organizationId, PurchaseReportQuery query, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        (query.From, query.To) = InventoryReportService.Period(from, to);
        query.Search = Text.Clean(query.Search);
        return reports.GetRegisterAsync(organizationId, query, cancellationToken);
    }

    public async Task<ReportPage<PurchaseByProductRow>> GetByProductAsync(int organizationId, PurchaseReportQuery query, DateOnly? from, DateOnly? to, int? categoryId, CancellationToken cancellationToken = default)
    {
        (query.From, query.To) = InventoryReportService.Period(from, to);
        query.Search = Text.Clean(query.Search);
        query.CategoryIds = categoryId is null ? null : await inventorySetup.GetCategoryTreeIdsAsync(organizationId, categoryId.Value, cancellationToken);
        return await reports.GetByProductAsync(organizationId, query, cancellationToken);
    }
}
