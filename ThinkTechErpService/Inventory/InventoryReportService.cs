using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Inventory;

public interface IInventoryReportService
{
    Task<ReportPage<StockOnHandRow>> GetStockOnHandAsync(int organizationId, StockReportQuery query, int? categoryId, CancellationToken cancellationToken = default);
    Task<StockCard?> GetStockCardAsync(int organizationId, int productId, int? warehouseId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockValuationRow>> GetValuationAsync(int organizationId, string? groupBy, int? warehouseId, CancellationToken cancellationToken = default);
    Task<ReportPage<StockMovementRow>> GetMovementsAsync(int organizationId, MovementReportQuery query, CancellationToken cancellationToken = default);
    Task<ReportPage<ReorderRow>> GetReorderAsync(int organizationId, StockReportQuery query, int? categoryId, CancellationToken cancellationToken = default);
    Task<ReportPage<ExpiryRow>> GetExpiryAsync(int organizationId, int? withinDays, int? warehouseId, string? search, int limit, int offset, CancellationToken cancellationToken = default);
}

public sealed class InventoryReportService(IInventoryReportRepository reports, IInventorySetupRepository setup) : IInventoryReportService
{
    public async Task<ReportPage<StockOnHandRow>> GetStockOnHandAsync(int organizationId, StockReportQuery query, int? categoryId, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.CategoryIds = await CategoryScopeAsync(organizationId, categoryId, cancellationToken);
        return await reports.GetStockOnHandAsync(organizationId, query, cancellationToken);
    }

    public async Task<StockCard?> GetStockCardAsync(int organizationId, int productId, int? warehouseId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (start, end) = Period(from, to);
        return await reports.GetStockCardAsync(organizationId, productId, warehouseId, start, end, cancellationToken);
    }

    public Task<IReadOnlyList<StockValuationRow>> GetValuationAsync(int organizationId, string? groupBy, int? warehouseId, CancellationToken cancellationToken = default)
        => reports.GetValuationAsync(organizationId, groupBy is "warehouse" or "brand" ? groupBy : "category", warehouseId, cancellationToken);

    public Task<ReportPage<StockMovementRow>> GetMovementsAsync(int organizationId, MovementReportQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        if (query.From > query.To)
            throw new BusinessRuleException("The start date must be on or before the end date.");
        return reports.GetMovementsAsync(organizationId, query, cancellationToken);
    }

    public async Task<ReportPage<ReorderRow>> GetReorderAsync(int organizationId, StockReportQuery query, int? categoryId, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.CategoryIds = await CategoryScopeAsync(organizationId, categoryId, cancellationToken);
        return await reports.GetReorderAsync(organizationId, query, cancellationToken);
    }

    public async Task<ReportPage<ExpiryRow>> GetExpiryAsync(int organizationId, int? withinDays, int? warehouseId, string? search, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var days = withinDays ?? (await setup.GetSettingAsync(organizationId, cancellationToken)).ExpiryAlertDays;
        return await reports.GetExpiryAsync(organizationId, days, warehouseId, Text.Clean(search), limit, offset, cancellationToken);
    }

    private async Task<IReadOnlyCollection<int>?> CategoryScopeAsync(int organizationId, int? categoryId, CancellationToken cancellationToken)
        => categoryId is null ? null : await setup.GetCategoryTreeIdsAsync(organizationId, categoryId.Value, cancellationToken);

    /// <summary>The requested dates, defaulting to the current month so far.</summary>
    public static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end)
            throw new BusinessRuleException("The start date must be on or before the end date.");
        return (start, end);
    }
}
