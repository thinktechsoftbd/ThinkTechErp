using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Sales;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Sales;

public interface ISalesSetupService
{
    Task<PagedResult<Customer>> GetCustomersAsync(int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Customer?> GetCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default);
    Task<Customer> CreateCustomerAsync(int organizationId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<Customer?> UpdateCustomerAsync(int organizationId, int customerId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);
}

public sealed class SalesSetupService(ISalesSetupRepository repository) : ISalesSetupService
{
    public Task<PagedResult<Customer>> GetCustomersAsync(int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetCustomersAsync(organizationId, Text.Clean(search), isActive, groupId, limit, offset, cancellationToken);

    public Task<Customer?> GetCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
        => repository.GetCustomerAsync(organizationId, customerId, cancellationToken);

    public Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
        => repository.GetCustomerOptionsAsync(organizationId, Text.Clean(search), ids is { Count: > 0 } ? ids : null, limit, cancellationToken);

    public async Task<Customer> CreateCustomerAsync(int organizationId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateCustomerAsync(organizationId, request, userId, cancellationToken));
        return (await repository.GetCustomerAsync(organizationId, id, cancellationToken))!;
    }

    public async Task<Customer?> UpdateCustomerAsync(int organizationId, int customerId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(organizationId, customerId, request, cancellationToken);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateCustomerAsync(organizationId, customerId, request, userId, cancellationToken))
            ? await repository.GetCustomerAsync(organizationId, customerId, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
        => repository.DeleteCustomerAsync(organizationId, customerId, cancellationToken);

    private async Task NormalizeAsync(int organizationId, int? customerId, CustomerUpsertRequest request, CancellationToken cancellationToken)
    {
        request.Name = request.Name.Trim();
        foreach (var property in typeof(CustomerUpsertRequest).GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name != "Name"))
            property.SetValue(request, Text.Clean((string?)property.GetValue(request)));
        request.CountryCode = request.CountryCode?.ToUpperInvariant();
        if (request.Name.Length == 0)
            throw new BusinessRuleException("Enter the customer's name.");
        if (request.OpeningBalance == 0)
            request.OpeningBalanceDate = null;

        // An empty code keeps the current one on update, and takes the next in the series on create.
        if (request.Code is not null && await repository.CustomerCodeExistsAsync(organizationId, request.Code, customerId, cancellationToken))
            throw new BusinessConflictException("A customer with this code already exists.");
        if (request.CustomerGroupLookupDataId is not null &&
            !await repository.LookupValueExistsAsync(request.CustomerGroupLookupDataId.Value, SalesRules.CustomerGroupLookupTable, cancellationToken))
            throw new BusinessRuleException("Choose a customer group from the Customer Group lookup list.");
    }
}
