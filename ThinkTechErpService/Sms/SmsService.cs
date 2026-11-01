using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpRepository.Sms;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Sms;

public interface ISmsService
{
    Task<SmsOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsProvider>> GetProvidersAsync(bool? isActive, CancellationToken cancellationToken = default);
    Task<SmsProvider?> GetProviderAsync(int id, CancellationToken cancellationToken = default);
    Task<SmsProvider> CreateProviderAsync(SmsProviderUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SmsProvider?> UpdateProviderAsync(int id, SmsProviderUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteProviderAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<SmsPurchase>> GetPurchasesAsync(SmsListQuery query, CancellationToken cancellationToken = default);
    Task<SmsPurchase?> GetPurchaseAsync(long id, CancellationToken cancellationToken = default);
    Task<SmsPurchase> CreatePurchaseAsync(SmsPurchaseUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SmsPurchase?> UpdatePurchaseAsync(long id, SmsPurchaseUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePurchaseAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<SmsAllocation>> GetAllocationsAsync(SmsListQuery query, CancellationToken cancellationToken = default);
    Task<SmsAllocation?> GetAllocationAsync(long id, CancellationToken cancellationToken = default);
    Task<SmsAllocation> CreateAllocationAsync(SmsAllocationUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<SmsAllocation?> UpdateAllocationAsync(long id, SmsAllocationUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteAllocationAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<SmsOrganizationBalance>> GetOrganizationBalancesAsync(string? search, bool onlyAllocated, int limit, int offset, CancellationToken cancellationToken = default);
    Task<SmsOrganizationBalance?> GetOrganizationBalanceAsync(Guid objectId, CancellationToken cancellationToken = default);
    Task<SmsOrganizationBalance?> AddUsageAsync(Guid objectId, long smsCount, CancellationToken cancellationToken = default);
    Task<SmsOrganizationBalance?> SetUsageAsync(Guid objectId, long smsUsage, CancellationToken cancellationToken = default);
    /// <summary>The organization's balance for an integration client; null unless the client created it.</summary>
    Task<SmsBalance?> GetClientBalanceAsync(Guid objectId, int clientUserId, CancellationToken cancellationToken = default);
    /// <summary>Records SMS an integration client sent; null unless the client created the organization.</summary>
    Task<SmsBalance?> AddClientUsageAsync(Guid objectId, long smsCount, int clientUserId, CancellationToken cancellationToken = default);
}

public sealed class SmsService(ISmsRepository repository) : ISmsService
{
    public Task<SmsOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        => repository.GetOverviewAsync(cancellationToken);

    // ---- Providers

    public Task<IReadOnlyList<SmsProvider>> GetProvidersAsync(bool? isActive, CancellationToken cancellationToken = default)
        => repository.GetProvidersAsync(isActive, cancellationToken);

    public Task<SmsProvider?> GetProviderAsync(int id, CancellationToken cancellationToken = default)
        => repository.GetProviderAsync(id, cancellationToken);

    public async Task<SmsProvider> CreateProviderAsync(SmsProviderUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        request.ApiPassword = Text.Clean(request.ApiPassword);
        request.ApiKey = Text.Clean(request.ApiKey);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreateProviderAsync(request, userId, cancellationToken));
        return (await repository.GetProviderAsync(id, cancellationToken))!;
    }

    public async Task<SmsProvider?> UpdateProviderAsync(int id, SmsProviderUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        // Secrets are write-only: null keeps the stored one, empty removes it.
        var clearPassword = request.ApiPassword is not null && Text.Clean(request.ApiPassword) is null;
        var clearKey = request.ApiKey is not null && Text.Clean(request.ApiKey) is null;
        request.ApiPassword = Text.Clean(request.ApiPassword);
        request.ApiKey = Text.Clean(request.ApiKey);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdateProviderAsync(id, request, clearPassword, clearKey, userId, cancellationToken))
            ? await repository.GetProviderAsync(id, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeleteProviderAsync(int id, CancellationToken cancellationToken = default)
        => repository.DeleteProviderAsync(id, cancellationToken);

    private static void Normalize(SmsProviderUpsertRequest request)
    {
        request.Name = request.Name.Trim();
        request.Code = request.Code.Trim().ToUpperInvariant();
        request.ApiBaseUrl = Text.Clean(request.ApiBaseUrl);
        request.ApiUsername = Text.Clean(request.ApiUsername);
        if (request.Name.Length == 0 || request.Code.Length == 0)
            throw new BusinessRuleException("Enter the provider's name and code.");
    }

    // ---- Purchases

    public Task<PagedResult<SmsPurchase>> GetPurchasesAsync(SmsListQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return repository.GetPurchasesAsync(query, cancellationToken);
    }

    public Task<SmsPurchase?> GetPurchaseAsync(long id, CancellationToken cancellationToken = default)
        => repository.GetPurchaseAsync(id, cancellationToken);

    public async Task<SmsPurchase> CreatePurchaseAsync(SmsPurchaseUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var purchase = await BuildPurchaseAsync(request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.SavePurchaseAsync(null, purchase, userId, cancellationToken));
        return (await repository.GetPurchaseAsync(id, cancellationToken))!;
    }

    public async Task<SmsPurchase?> UpdatePurchaseAsync(long id, SmsPurchaseUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await repository.GetPurchaseAsync(id, cancellationToken) is null)
            return null;
        var purchase = await BuildPurchaseAsync(request, cancellationToken);
        await BusinessDbErrors.ExecuteAsync(() => repository.SavePurchaseAsync(id, purchase, userId, cancellationToken));
        return await repository.GetPurchaseAsync(id, cancellationToken);
    }

    public Task<DeleteOutcome> DeletePurchaseAsync(long id, CancellationToken cancellationToken = default)
        => repository.DeletePurchaseAsync(id, cancellationToken);

    /// <summary>The total is quantity x unit price unless the invoice says otherwise; BDT purchases have rate 1.</summary>
    private async Task<SmsPurchase> BuildPurchaseAsync(SmsPurchaseUpsertRequest request, CancellationToken cancellationToken)
    {
        var currency = await repository.GetCurrencyCodeAsync(request.CurrencyId, SettingsRules.CurrencyLookupTable, cancellationToken)
            ?? throw new BusinessRuleException("Choose a currency from the Currency lookup list.");
        var rate = string.Equals(currency, SmsRules.LocalCurrency, StringComparison.OrdinalIgnoreCase) ? 1 : request.LocalExchangeRate;
        var total = Math.Round(request.TotalAmount ?? request.SmsQuantity * request.UnitPrice, 2, MidpointRounding.AwayFromZero);
        return new SmsPurchase
        {
            ProviderId = request.ProviderId,
            PurchaseDate = request.PurchaseDate,
            InvoiceNumber = Text.Clean(request.InvoiceNumber),
            ReferenceNumber = Text.Clean(request.ReferenceNumber),
            SmsQuantity = request.SmsQuantity,
            CurrencyId = request.CurrencyId,
            UnitPrice = request.UnitPrice,
            TotalAmount = total,
            LocalExchangeRate = rate,
            LocalTotalAmount = Math.Round(total * rate, 2, MidpointRounding.AwayFromZero),
            Remarks = Text.Clean(request.Remarks)
        };
    }

    // ---- Allocations

    public Task<PagedResult<SmsAllocation>> GetAllocationsAsync(SmsListQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        return repository.GetAllocationsAsync(query, cancellationToken);
    }

    public Task<SmsAllocation?> GetAllocationAsync(long id, CancellationToken cancellationToken = default)
        => repository.GetAllocationAsync(id, cancellationToken);

    public async Task<SmsAllocation> CreateAllocationAsync(SmsAllocationUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        request.Remarks = Text.Clean(request.Remarks);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.SaveAllocationAsync(null, request, userId, cancellationToken));
        return (await repository.GetAllocationAsync(id, cancellationToken))!;
    }

    public async Task<SmsAllocation?> UpdateAllocationAsync(long id, SmsAllocationUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAllocationAsync(id, cancellationToken) is null)
            return null;
        request.Remarks = Text.Clean(request.Remarks);
        await BusinessDbErrors.ExecuteAsync(() => repository.SaveAllocationAsync(id, request, userId, cancellationToken));
        return await repository.GetAllocationAsync(id, cancellationToken);
    }

    public Task<DeleteOutcome> DeleteAllocationAsync(long id, CancellationToken cancellationToken = default)
        => repository.DeleteAllocationAsync(id, cancellationToken);

    // ---- Organization balances and usage

    public Task<PagedResult<SmsOrganizationBalance>> GetOrganizationBalancesAsync(string? search, bool onlyAllocated, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetOrganizationBalancesAsync(Text.Clean(search), onlyAllocated, limit, offset, cancellationToken);

    public Task<SmsOrganizationBalance?> GetOrganizationBalanceAsync(Guid objectId, CancellationToken cancellationToken = default)
        => repository.GetOrganizationBalanceAsync(objectId, cancellationToken);

    public async Task<SmsOrganizationBalance?> AddUsageAsync(Guid objectId, long smsCount, CancellationToken cancellationToken = default)
        => await repository.GetOrganizationBalanceAsync(objectId, cancellationToken) is null
            ? null
            : await repository.AddUsageAsync(objectId, smsCount, cancellationToken);

    public async Task<SmsOrganizationBalance?> SetUsageAsync(Guid objectId, long smsUsage, CancellationToken cancellationToken = default)
        => await repository.GetOrganizationBalanceAsync(objectId, cancellationToken) is null
            ? null
            : await repository.SetUsageAsync(objectId, smsUsage, cancellationToken);

    public async Task<SmsBalance?> GetClientBalanceAsync(Guid objectId, int clientUserId, CancellationToken cancellationToken = default)
    {
        if (!await repository.IsOrganizationCreatedByAsync(objectId, clientUserId, cancellationToken))
            return null;
        var balance = await repository.GetOrganizationBalanceAsync(objectId, cancellationToken);
        return balance is null ? null : ToClientBalance(balance);
    }

    public async Task<SmsBalance?> AddClientUsageAsync(Guid objectId, long smsCount, int clientUserId, CancellationToken cancellationToken = default)
    {
        if (!await repository.IsOrganizationCreatedByAsync(objectId, clientUserId, cancellationToken))
            return null;
        return ToClientBalance(await repository.AddUsageAsync(objectId, smsCount, cancellationToken));
    }

    private static SmsBalance ToClientBalance(SmsOrganizationBalance balance) => new()
    {
        OrganizationObjectId = balance.ObjectId,
        AllocatedSms = balance.AllocatedSms,
        SmsUsage = balance.SmsUsage,
        AvailableSms = balance.AvailableSms
    };
}
