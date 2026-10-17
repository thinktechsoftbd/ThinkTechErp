using ThinkTechErpDomain.Models.Sms;

namespace ThinkTechErpService.Sms;

public interface ISmsService
{
    Task<SmsPlatformBalance> GetPlatformBalanceAsync(CancellationToken cancellationToken = default);
    Task<SmsClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsClient>> GetClientsAsync(int limit, int offset, CancellationToken cancellationToken = default);
    Task<SmsClient> CreateClientAsync(SmsClientCreateRequest request, CancellationToken cancellationToken = default);
    Task<SmsClient?> UpdateClientAsync(string clientId, SmsClientUpdateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsAdnPurchase>> GetPurchasesAsync(int limit, int offset, CancellationToken cancellationToken = default);
    Task<long> RecordPurchaseAsync(SmsPurchaseRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsClientAllocation>> GetAllocationsAsync(string? clientId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<long> AllocateAsync(string clientId, string clientName, SmsAllocationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsUsageRecord>> GetUsageAsync(string? clientId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<long> RecordUsageAsync(string clientId, SmsUsageRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsBalanceTransaction>> GetTransactionsAsync(string? clientId, int limit, int offset, CancellationToken cancellationToken = default);
}
