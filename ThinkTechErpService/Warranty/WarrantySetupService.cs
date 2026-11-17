using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Warranty;
using ThinkTechErpService.Common;
using ThinkTechErpService.Purchase;
using ThinkTechErpService.Sales;

namespace ThinkTechErpService.Warranty;

public interface IWarrantySetupService
{
    Task<PagedResult<WarrantyPolicy>> GetPoliciesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<WarrantyPolicy?> GetPolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WarrantyPolicyOption>> GetPolicyOptionsAsync(int organizationId, bool includeInactive, CancellationToken cancellationToken = default);
    Task<WarrantyPolicy> CreatePolicyAsync(int organizationId, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<WarrantyPolicy?> UpdatePolicyAsync(int organizationId, int id, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default);

    Task<PagedResult<ProductWarrantyItem>> GetProductsAsync(int organizationId, ProductWarrantyQuery query, CancellationToken cancellationToken = default);
    Task<int> AssignPolicyAsync(int organizationId, ProductWarrantyAssignRequest request, int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OptionItem>> GetDeviceTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OptionItem>> GetTechniciansAsync(int organizationId, CancellationToken cancellationToken = default);
    /// <summary>Every party (customers and suppliers), as the sales screen lists them.</summary>
    Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierOption>> GetSupplierOptionsAsync(int organizationId, string? search, int limit, CancellationToken cancellationToken = default);
}

public sealed class WarrantySetupService(
    IWarrantySetupRepository repository,
    ISalesSetupService salesSetup,
    IPurchaseSetupService purchaseSetup) : IWarrantySetupService
{
    public Task<PagedResult<WarrantyPolicy>> GetPoliciesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetPoliciesAsync(organizationId, Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<WarrantyPolicy?> GetPolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default)
        => repository.GetPolicyAsync(organizationId, id, cancellationToken);

    public Task<IReadOnlyList<WarrantyPolicyOption>> GetPolicyOptionsAsync(int organizationId, bool includeInactive, CancellationToken cancellationToken = default)
        => repository.GetPolicyOptionsAsync(organizationId, includeInactive, cancellationToken);

    private static void Normalize(WarrantyPolicyUpsertRequest request)
    {
        request.Name = Text.Clean(request.Name) ?? throw new BusinessRuleException("Name the policy, e.g. \"1 Year Warranty\".");
        request.Terms = Text.Clean(request.Terms);
        if (request.ReplacementDays > request.WarrantyMonths * 31)
            throw new BusinessRuleException("The replacement period can't be longer than the warranty.");
    }

    public async Task<WarrantyPolicy> CreatePolicyAsync(int organizationId, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreatePolicyAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetPolicyAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<WarrantyPolicy?> UpdatePolicyAsync(int organizationId, int id, WarrantyPolicyUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        // Cards already issued keep the terms they were sold with; only later sales use the change.
        var found = await BusinessDbErrors.ExecuteAsync(() => repository.UpdatePolicyAsync(organizationId, id, request, userId, cancellationToken));
        return found ? await repository.GetPolicyAsync(organizationId, id, cancellationToken) : null;
    }

    public Task<DeleteOutcome> DeletePolicyAsync(int organizationId, int id, CancellationToken cancellationToken = default)
        => repository.DeletePolicyAsync(organizationId, id, cancellationToken);

    public Task<PagedResult<ProductWarrantyItem>> GetProductsAsync(int organizationId, ProductWarrantyQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return repository.GetProductsAsync(organizationId, query, cancellationToken);
    }

    public async Task<int> AssignPolicyAsync(int organizationId, ProductWarrantyAssignRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var ids = request.ProductIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            throw new BusinessRuleException("Choose at least one product.");
        return await BusinessDbErrors.ExecuteAsync(() => repository.AssignPolicyAsync(organizationId, ids, request.WarrantyPolicyId, userId, cancellationToken));
    }

    public Task<IReadOnlyList<OptionItem>> GetDeviceTypesAsync(CancellationToken cancellationToken = default)
        => repository.GetDeviceTypesAsync(cancellationToken);

    public Task<IReadOnlyList<OptionItem>> GetTechniciansAsync(int organizationId, CancellationToken cancellationToken = default)
        => repository.GetTechniciansAsync(organizationId, cancellationToken);

    public Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(
        int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
        => salesSetup.GetCustomerOptionsAsync(organizationId, search, ids, limit, includeSuppliers: true, cancellationToken);

    public Task<IReadOnlyList<SupplierOption>> GetSupplierOptionsAsync(int organizationId, string? search, int limit, CancellationToken cancellationToken = default)
        => purchaseSetup.GetSupplierOptionsAsync(organizationId, search, limit, cancellationToken);
}
