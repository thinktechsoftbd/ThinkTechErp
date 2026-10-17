using Npgsql;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpRepository.Sms;

namespace ThinkTechErpService.Sms;

public sealed class SmsService(ISmsRepository repository) : ISmsService
{
    public Task<SmsPlatformBalance> GetPlatformBalanceAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetPlatformBalanceAsync(cancellationToken));

    public Task<SmsClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetClientAsync(clientId, cancellationToken));

    public Task<IReadOnlyList<SmsClient>> GetClientsAsync(int limit, int offset, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetClientsAsync(limit, offset, cancellationToken));

    public Task<SmsClient> CreateClientAsync(SmsClientCreateRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.CreateClientAsync(request, cancellationToken));

    public Task<SmsClient?> UpdateClientAsync(
        string clientId,
        SmsClientUpdateRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.UpdateClientAsync(clientId, request, cancellationToken));

    public Task<IReadOnlyList<SmsAdnPurchase>> GetPurchasesAsync(int limit, int offset, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetPurchasesAsync(limit, offset, cancellationToken));

    public Task<long> RecordPurchaseAsync(SmsPurchaseRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.RecordPurchaseAsync(request, cancellationToken));

    public Task<IReadOnlyList<SmsClientAllocation>> GetAllocationsAsync(
        string? clientId,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetAllocationsAsync(clientId, limit, offset, cancellationToken));

    public Task<long> AllocateAsync(
        string clientId,
        string clientName,
        SmsAllocationRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.AllocateAsync(clientId, clientName, request, cancellationToken));

    public Task<IReadOnlyList<SmsUsageRecord>> GetUsageAsync(
        string? clientId,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetUsageAsync(clientId, limit, offset, cancellationToken));

    public Task<long> RecordUsageAsync(
        string clientId,
        SmsUsageRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.RecordUsageAsync(clientId, request, cancellationToken));

    public Task<IReadOnlyList<SmsBalanceTransaction>> GetTransactionsAsync(
        string? clientId,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => repository.GetTransactionsAsync(clientId, limit, offset, cancellationToken));

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PostgresException exception) when (exception.SqlState is "P0001" or "23505")
        {
            throw new SmsConflictException(
                exception.SqlState == "P0001"
                    ? "The SMS balance operation was rejected by the account rules."
                    : "The request conflicts with an existing client or reference.",
                exception);
        }
    }
}
