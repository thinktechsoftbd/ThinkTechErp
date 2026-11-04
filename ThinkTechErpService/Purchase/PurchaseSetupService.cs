using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Purchase;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Purchase;

public interface IPurchaseSetupService
{
    Task<PagedResult<Supplier>> GetSuppliersAsync(int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Supplier?> GetSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierOption>> GetSupplierOptionsAsync(int organizationId, string? search, int limit, CancellationToken cancellationToken = default);
    Task<Supplier> CreateSupplierAsync(int organizationId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Supplier?> UpdateSupplierAsync(int organizationId, int supplierId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default);

    Task<PagedResult<TaxRate>> GetTaxRatesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<TaxRate?> GetTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default);
    Task<TaxRate> CreateTaxRateAsync(int organizationId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<TaxRate?> UpdateTaxRateAsync(int organizationId, int taxRateId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default);

    Task<PagedResult<TaxGroup>> GetTaxGroupsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<TaxGroup?> GetTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default);
    Task<TaxGroup> CreateTaxGroupAsync(int organizationId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<TaxGroup?> UpdateTaxGroupAsync(int organizationId, int taxGroupId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default);
}

public sealed class PurchaseSetupService(IPurchaseSetupRepository repository) : IPurchaseSetupService
{
    // ---- Suppliers

    public Task<PagedResult<Supplier>> GetSuppliersAsync(int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetSuppliersAsync(organizationId, Text.Clean(search), isActive, groupId, limit, offset, cancellationToken);

    public Task<Supplier?> GetSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default)
        => repository.GetSupplierAsync(organizationId, supplierId, cancellationToken);

    public Task<IReadOnlyList<SupplierOption>> GetSupplierOptionsAsync(int organizationId, string? search, int limit, CancellationToken cancellationToken = default)
        => repository.GetSupplierOptionsAsync(organizationId, Text.Clean(search), limit, cancellationToken);

    public async Task<Supplier> CreateSupplierAsync(int organizationId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateSupplierAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetSupplierAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Supplier?> UpdateSupplierAsync(int organizationId, int supplierId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, supplierId, request, cancellationToken);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateSupplierAsync(organizationId, supplierId, request, userId, cancellationToken))
            ? await repository.GetSupplierAsync(organizationId, supplierId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default)
        => repository.DeleteSupplierAsync(organizationId, supplierId, cancellationToken);

    private async Task NormalizeAsync(int organizationId, int? supplierId, SupplierUpsertRequest request, CancellationToken cancellationToken)
    {
        request.Name = request.Name.Trim();
        foreach (var property in typeof(SupplierUpsertRequest).GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name != "Name"))
            property.SetValue(request, Text.Clean((string?)property.GetValue(request)));
        request.CountryCode = request.CountryCode?.ToUpperInvariant();
        if (request.OpeningBalance == 0)
            request.OpeningBalanceDate = null;
        if (request.Phone is null)
            throw new BusinessRuleException("Enter the supplier's phone.");
        if (request.AddressLine1 is null)
            throw new BusinessRuleException("Enter the supplier's address line 1.");

        // An empty code keeps the current one on update, and takes the next in the series on create.
        if (request.Code is not null && await repository.SupplierCodeExistsAsync(organizationId, request.Code, supplierId, cancellationToken))
            throw new BusinessConflictException("A supplier with this code already exists.");
        if (request.SupplierGroupLookupDataId is not null &&
            !await repository.LookupValueExistsAsync(request.SupplierGroupLookupDataId.Value, PurchaseRules.SupplierGroupLookupTable, cancellationToken))
            throw new BusinessRuleException("Choose a supplier group from the Supplier Group lookup list.");
        if (request.CurrencyLookupDataId is not null &&
            !await repository.LookupValueExistsAsync(request.CurrencyLookupDataId.Value, SettingsRules.CurrencyLookupTable, cancellationToken))
            throw new BusinessRuleException("Choose a currency from the Currency lookup list.");
    }

    // ---- Tax rates

    public Task<PagedResult<TaxRate>> GetTaxRatesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetTaxRatesAsync(organizationId, Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<TaxRate?> GetTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default)
        => repository.GetTaxRateAsync(organizationId, taxRateId, cancellationToken);

    public async Task<TaxRate> CreateTaxRateAsync(int organizationId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateTaxRateAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetTaxRateAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<TaxRate?> UpdateTaxRateAsync(int organizationId, int taxRateId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateTaxRateAsync(organizationId, taxRateId, request, userId, cancellationToken))
            ? await repository.GetTaxRateAsync(organizationId, taxRateId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default)
        => repository.DeleteTaxRateAsync(organizationId, taxRateId, cancellationToken);

    private static void Normalize(TaxRateUpsertRequest request)
    {
        if (!PurchaseRules.TaxTypes.Contains(request.TaxType))
            throw new BusinessRuleException($"Tax type must be one of: {string.Join(", ", PurchaseRules.TaxTypes)}.");
        request.Code = request.Code.Trim();
        request.Name = request.Name.Trim();
    }

    // ---- Tax groups

    public async Task<PagedResult<TaxGroup>> GetTaxGroupsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var page = await repository.GetTaxGroupsAsync(organizationId, Text.Clean(search), isActive, limit, offset, cancellationToken);
        foreach (var group in page.Items)
            group.EffectiveRatePercent = Math.Round(TaxCalculator.EffectiveRate(group.Rates) * 100, 4);
        return page;
    }

    public async Task<TaxGroup?> GetTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default)
    {
        var group = await repository.GetTaxGroupAsync(organizationId, taxGroupId, cancellationToken);
        if (group is not null)
            group.EffectiveRatePercent = Math.Round(TaxCalculator.EffectiveRate(group.Rates) * 100, 4);
        return group;
    }

    public async Task<TaxGroup> CreateTaxGroupAsync(int organizationId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateTaxGroupAsync(organizationId, request, userId, cancellationToken));
        return (await GetTaxGroupAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<TaxGroup?> UpdateTaxGroupAsync(int organizationId, int taxGroupId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, request, cancellationToken);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateTaxGroupAsync(organizationId, taxGroupId, request, userId, cancellationToken))
            ? await GetTaxGroupAsync(organizationId, taxGroupId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default)
        => repository.DeleteTaxGroupAsync(organizationId, taxGroupId, cancellationToken);

    private async Task NormalizeAsync(int organizationId, TaxGroupUpsertRequest request, CancellationToken cancellationToken)
    {
        request.Name = request.Name.Trim();
        var ids = request.Rates.Select(r => r.TaxRateId).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new BusinessRuleException("A tax rate is listed twice in this group.");
        if (ids.Count > 0 && await repository.CountTaxRatesAsync(organizationId, ids, cancellationToken) != ids.Count)
            throw new BusinessRuleException("One of the tax rates doesn't exist.");
        if (request.Rates.FirstOrDefault()?.IsCompound == true)
            throw new BusinessRuleException("The first rate can't be compound: there is no tax before it.");
    }
}
