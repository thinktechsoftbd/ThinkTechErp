using Dapper;
using ThinkTechErpDomain.Models.Sms;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Sms;

public sealed class SmsRepository(IDbConnectionFactory connectionFactory) : ISmsRepository
{
    private const string ClientSelect = """
        SELECT client_id AS ClientId,
               display_name AS DisplayName,
               is_active AS IsActive,
               available_sms AS AvailableSms,
               total_allocated_sms AS TotalAllocatedSms,
               total_consumed_sms AS TotalConsumedSms,
               created_at AS CreatedAt,
               updated_at AS UpdatedAt
        FROM sms.client_balance_summary
        """;

    public async Task<SmsPlatformBalance> GetPlatformBalanceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<SmsPlatformBalance>(new CommandDefinition("""
            SELECT total_purchased_sms AS TotalPurchasedSms,
                   unallocated_sms AS UnallocatedSms,
                   assigned_available_sms AS AssignedAvailableSms,
                   consumed_sms AS ConsumedSms,
                   total_available_sms AS TotalAvailableSms,
                   updated_at AS UpdatedAt
            FROM sms.platform_balance_summary
            """, cancellationToken: cancellationToken));
    }

    public async Task<SmsClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleOrDefaultAsync<SmsClient>(new CommandDefinition(
            $"{ClientSelect} WHERE client_id = @ClientId",
            new { ClientId = clientId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SmsClient>> GetClientsAsync(
        int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        var rows = await connection.QueryAsync<SmsClient>(new CommandDefinition(
            $"{ClientSelect} ORDER BY created_at DESC, client_id LIMIT @Limit OFFSET @Offset",
            new { Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SmsClient> CreateClientAsync(
        SmsClientCreateRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT sms.register_client(@ClientId, @DisplayName)", request,
            cancellationToken: cancellationToken));
        return await GetClientAsync(request.ClientId, cancellationToken)
            ?? throw new InvalidOperationException("Created SMS client could not be read back.");
    }

    public async Task<SmsClient?> UpdateClientAsync(
        string clientId, SmsClientUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DisplayName is null && request.IsActive is null)
            throw new ArgumentException("Provide a display name or active status to update.", nameof(request));

        await using var connection = connectionFactory.CreateConnection("SmsApi");
        var updated = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT sms.update_client(@ClientId, @DisplayName, @IsActive)
            """, new { ClientId = clientId, request.DisplayName, request.IsActive },
            cancellationToken: cancellationToken));
        return updated ? await GetClientAsync(clientId, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<SmsAdnPurchase>> GetPurchasesAsync(
        int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        var rows = await connection.QueryAsync<SmsAdnPurchase>(new CommandDefinition("""
            SELECT purchase_id AS PurchaseId,
                   transaction_id AS TransactionId,
                   provider AS Provider,
                   provider_reference AS ProviderReference,
                   sms_quantity AS SmsQuantity,
                   amount_paid AS AmountPaid,
                   currency AS Currency,
                   exchange_rate AS ExchangeRate,
                   purchased_at AS PurchasedAt,
                   note AS Note
            FROM sms.adn_purchases
            ORDER BY purchased_at DESC, purchase_id DESC
            LIMIT @Limit OFFSET @Offset
            """, new { Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<long> RecordPurchaseAsync(
        SmsPurchaseRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT sms.record_adn_purchase(
                p_sms_quantity => @SmsQuantity,
                p_provider_reference => @ProviderReference,
                p_amount_paid => @AmountPaid,
                p_currency => CAST(@Currency AS char(3)),
                p_exchange_rate => @ExchangeRate,
                p_note => @Note)
            """, request, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdatePurchaseAsync(
        long purchaseId, SmsPurchaseUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT sms.update_adn_purchase(
                p_purchase_id => @PurchaseId,
                p_provider_reference => @ProviderReference,
                p_amount_paid => @AmountPaid,
                p_currency => CAST(@Currency AS char(3)),
                p_exchange_rate => @ExchangeRate,
                p_note => @Note)
            """, new
            {
                PurchaseId = purchaseId,
                request.ProviderReference,
                request.AmountPaid,
                request.Currency,
                request.ExchangeRate,
                request.Note
            }, cancellationToken: cancellationToken));
    }
    public async Task<IReadOnlyList<SmsClientAllocation>> GetAllocationsAsync(
        string? clientId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        var rows = await connection.QueryAsync<SmsClientAllocation>(new CommandDefinition("""
            SELECT a.allocation_id AS AllocationId,
                   a.transaction_id AS TransactionId,
                   a.client_id AS ClientId,
                   c.display_name AS ClientName,
                   a.sms_quantity AS SmsQuantity,
                   a.note AS Note,
                   a.is_finalize AS IsFinalized,
                   a.allocated_at AS AllocatedAt
            FROM sms.client_allocations AS a
            INNER JOIN sms.clients AS c ON c.client_id = a.client_id
            WHERE CAST(@ClientId AS text) IS NULL OR a.client_id = CAST(@ClientId AS text)
            ORDER BY a.allocated_at DESC, a.allocation_id DESC
            """, new { ClientId = clientId, Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<long> AllocateAsync(
        string clientId, string clientName, SmsAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT sms.allocate_sms(
                p_client_id => @ClientId,
                p_client_name => @ClientName,
                p_sms_quantity => @SmsQuantity,
                p_note => @Note,
                p_is_finalized => @IsFinalized)
            """, new { ClientId = clientId, ClientName = clientName, request.SmsQuantity, request.Note, request.IsFinalized },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAllocationAsync(
        long allocationId, SmsAllocationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT sms.update_allocation_note(@AllocationId, @Note)",
            new { AllocationId = allocationId, request.Note }, cancellationToken: cancellationToken));
    }

    public async Task<bool> FinalizeAllocationAsync(long allocationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT sms.finalize_allocation(@AllocationId)",
            new { AllocationId = allocationId }, cancellationToken: cancellationToken));
    }
    public async Task<IReadOnlyList<SmsUsageRecord>> GetUsageAsync(
        string? clientId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        var rows = await connection.QueryAsync<SmsUsageRecord>(new CommandDefinition("""
            SELECT usage_id AS UsageId,
                   transaction_id AS TransactionId,
                   client_id AS ClientId,
                   request_reference AS RequestReference,
                   provider_message_id AS ProviderMessageId,
                   recipient AS Recipient,
                   sms_quantity AS SmsQuantity,
                   sent_at AS SentAt
            FROM sms.usage_records
            WHERE CAST(@ClientId AS text) IS NULL OR client_id = CAST(@ClientId AS text)
            ORDER BY sent_at DESC, usage_id DESC
            LIMIT @Limit OFFSET @Offset
            """, new { ClientId = clientId, Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<long> RecordUsageAsync(
        string clientId, SmsUsageRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        return await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT sms.consume_sms(@ClientId, @SmsQuantity, @RequestReference, @Recipient, @ProviderMessageId)
            """, new
            {
                ClientId = clientId,
                request.SmsQuantity,
                request.RequestReference,
                request.Recipient,
                request.ProviderMessageId
            }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SmsBalanceTransaction>> GetTransactionsAsync(
        string? clientId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection("SmsApi");
        var rows = await connection.QueryAsync<SmsBalanceTransaction>(new CommandDefinition("""
            SELECT transaction_id AS TransactionId,
                   transaction_type AS TransactionType,
                   quantity AS Quantity,
                   client_id AS ClientId,
                   platform_delta AS PlatformDelta,
                   client_delta AS ClientDelta,
                   platform_balance_after AS PlatformBalanceAfter,
                   client_balance_after AS ClientBalanceAfter,
                   provider_reference AS ProviderReference,
                   request_reference AS RequestReference,
                   note AS Note,
                   created_at AS CreatedAt
            FROM sms.balance_transactions
            WHERE CAST(@ClientId AS text) IS NULL OR client_id = CAST(@ClientId AS text)
            ORDER BY created_at DESC, transaction_id DESC
            LIMIT @Limit OFFSET @Offset
            """, new { ClientId = clientId, Limit = limit, Offset = offset }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
