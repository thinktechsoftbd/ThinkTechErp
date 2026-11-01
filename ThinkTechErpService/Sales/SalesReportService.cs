using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpRepository.Sales;
using ThinkTechErpService.Common;
using ThinkTechErpService.Inventory;

namespace ThinkTechErpService.Sales;

public interface ISalesReportService
{
    Task<CustomerLedger?> GetCustomerLedgerAsync(int organizationId, int customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<ReportPage<CustomerDueRow>> GetCustomerDuesAsync(int organizationId, SalesReportQuery query, DateOnly? asOf, CancellationToken cancellationToken = default);
    Task<ReportPage<SalesRegisterRow>> GetRegisterAsync(int organizationId, SalesReportQuery query, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<ReportPage<SalesByProductRow>> GetByProductAsync(int organizationId, SalesReportQuery query, DateOnly? from, DateOnly? to, int? categoryId, CancellationToken cancellationToken = default);
}

public sealed class SalesReportService(ISalesReportRepository reports, IInventorySetupRepository inventorySetup) : ISalesReportService
{
    public async Task<CustomerLedger?> GetCustomerLedgerAsync(int organizationId, int customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (start, end) = InventoryReportService.Period(from, to);
        return await reports.GetCustomerLedgerAsync(organizationId, customerId, start, end, cancellationToken);
    }

    public Task<ReportPage<CustomerDueRow>> GetCustomerDuesAsync(int organizationId, SalesReportQuery query, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return reports.GetCustomerDuesAsync(organizationId, query, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    }

    public Task<ReportPage<SalesRegisterRow>> GetRegisterAsync(int organizationId, SalesReportQuery query, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        (query.From, query.To) = InventoryReportService.Period(from, to);
        query.Search = Text.Clean(query.Search);
        return reports.GetRegisterAsync(organizationId, query, cancellationToken);
    }

    public async Task<ReportPage<SalesByProductRow>> GetByProductAsync(int organizationId, SalesReportQuery query, DateOnly? from, DateOnly? to, int? categoryId, CancellationToken cancellationToken = default)
    {
        (query.From, query.To) = InventoryReportService.Period(from, to);
        query.Search = Text.Clean(query.Search);
        query.CategoryIds = categoryId is null ? null : await inventorySetup.GetCategoryTreeIdsAsync(organizationId, categoryId.Value, cancellationToken);
        return await reports.GetByProductAsync(organizationId, query, cancellationToken);
    }
}
