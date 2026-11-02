using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

public interface IVoucherRepository
{
    Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default);
    Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates (Id 0, numbered 00000000001, ... per organization) or replaces a draft voucher. A line with the
    /// id of one of the draft's lines is updated in place, keeping its created stamp; the others are added and
    /// the draft's lines left out are removed. With post it is posted in the same transaction. Returns its id.
    /// </summary>
    Task<long> SaveVoucherAsync(int organizationId, Voucher voucher, bool post, int userId, CancellationToken cancellationToken = default);
    /// <summary>Flags a draft deleted (is_deleted); a posted or cancelled voucher answers InUse.</summary>
    Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
    Task CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default);
}

public sealed class VoucherRepository(IDbConnectionFactory connectionFactory) : IVoucherRepository
{
    // The voucher's type and payment method are lookup values; its parties are on the lines.
    private const string VoucherSelect = """
        SELECT v.voucher_id AS Id, v.object_id AS ObjectId, v.organization_id AS OrganizationId,
               v.voucher_ref_no AS VoucherRefNo, v.voucher_type_id AS VoucherTypeId, vt.lookup_data_name AS VoucherType,
               v.voucher_date AS VoucherDate, v.payment_method_id AS PaymentMethodId, pm.lookup_data_name AS PaymentMethod,
               v.cheque_no AS ChequeNo, v.cheque_date AS ChequeDate, v.pay_type AS PayType, v.narration AS Narration,
               (SELECT string_agg(DISTINCT p.supplier_name, ', ')
                FROM accounting.voucher_line pl
                JOIN purchase.supplier p ON p.supplier_id = pl.party_id
                WHERE pl.voucher_id = v.voucher_id) AS PartyNames,
               v.total_amount AS TotalAmount, v.status AS Status,
               v.posted_at AT TIME ZONE 'utc' AS PostedAt, v.posted_by AS PostedBy,
               v.cancelled_at AT TIME ZONE 'utc' AS CancelledAt, v.cancelled_by AS CancelledBy,
               v.created_at AT TIME ZONE 'utc' AS CreatedAt, v.created_by AS CreatedBy,
               v.updated_at AT TIME ZONE 'utc' AS UpdatedAt, v.updated_by AS UpdatedBy
        """;

    private const string VoucherFrom = """
        FROM accounting.voucher v
        JOIN public.lookup_data vt ON vt.lookup_data_id = v.voucher_type_id
        LEFT JOIN public.lookup_data pm ON pm.lookup_data_id = v.payment_method_id
        """;

    private const string VoucherFilter = """
        WHERE v.organization_id = @Org AND NOT v.is_deleted
          AND (CAST(@Pattern AS text) IS NULL OR v.voucher_ref_no ILIKE @Pattern OR v.cheque_no ILIKE @Pattern
               OR v.narration ILIKE @Pattern
               OR EXISTS (SELECT 1 FROM accounting.voucher_line sl
                          JOIN purchase.supplier sp ON sp.supplier_id = sl.party_id
                          WHERE sl.voucher_id = v.voucher_id AND sp.supplier_name ILIKE @Pattern))
          AND (CAST(@VoucherTypeId AS bigint) IS NULL OR v.voucher_type_id = @VoucherTypeId)
          AND (CAST(@BillTypeIds AS bigint[]) IS NULL
               OR (v.voucher_type_id = ANY(@BillTypeIds)
                   AND EXISTS (SELECT 1 FROM accounting.voucher_line bl
                               WHERE bl.voucher_id = v.voucher_id AND bl.party_id IS NOT NULL)))
          AND (CAST(@Status AS text) IS NULL OR v.status = @Status)
          AND (CAST(@PartyId AS int) IS NULL
               OR EXISTS (SELECT 1 FROM accounting.voucher_line ql WHERE ql.voucher_id = v.voucher_id AND ql.party_id = @PartyId))
          AND (CAST(@From AS date) IS NULL OR v.voucher_date >= @From)
          AND (CAST(@To AS date) IS NULL OR v.voucher_date <= @To)
        """;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<PagedResult<Voucher>> GetVouchersAsync(int organizationId, VoucherQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await RepositoryHelpers.PagedAsync<Voucher>(connection,
            $"SELECT count(*) {VoucherFrom} {VoucherFilter}",
            $"{VoucherSelect} {VoucherFrom} {VoucherFilter} ORDER BY v.voucher_date DESC, v.voucher_id DESC",
            new
            {
                Org = organizationId,
                Pattern = RepositoryHelpers.Pattern(query.Search),
                query.VoucherTypeId,
                BillTypeIds = query.BillTypeIds?.ToArray(),
                query.Status,
                query.PartyId,
                query.From,
                query.To
            }, query.Limit, query.Offset, cancellationToken);
    }

    public async Task<Voucher?> GetVoucherAsync(int organizationId, long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var voucher = await connection.QuerySingleOrDefaultAsync<Voucher>(new CommandDefinition(
            $"{VoucherSelect} {VoucherFrom} WHERE v.organization_id = @Org AND v.voucher_id = @Id AND NOT v.is_deleted",
            new { Org = organizationId, Id = id }, cancellationToken: cancellationToken));
        if (voucher is null)
            return null;

        voucher.Lines = (await connection.QueryAsync<VoucherLine>(new CommandDefinition("""
            SELECT l.voucher_line_id AS Id, l.line_no AS LineNo, l.acc_sub_class_id AS AccountId,
                   coalesce(trim(a.sub_code), '') AS AccountCode, coalesce(a.sub_name, '') AS AccountName,
                   l.party_id AS PartyId, p.supplier_code AS PartyCode, p.supplier_name AS PartyName,
                   l.debit_amount AS DebitAmount, l.credit_amount AS CreditAmount,
                   l.lk_currency_id AS CurrencyLookupDataId, cur.lookup_data_name AS Currency,
                   l.local_exchange_rate AS LocalExchangeRate, l.line_narration AS LineNarration,
                   l.created_at AT TIME ZONE 'utc' AS CreatedAt, l.created_by AS CreatedBy,
                   l.updated_at AT TIME ZONE 'utc' AS UpdatedAt, l.updated_by AS UpdatedBy
            FROM accounting.voucher_line l
            JOIN accounting.acc_sub_class a ON a.acc_sub_class_id = l.acc_sub_class_id
            LEFT JOIN purchase.supplier p ON p.supplier_id = l.party_id
            LEFT JOIN public.lookup_data cur ON cur.lookup_data_id = l.lk_currency_id
            WHERE l.voucher_id = @Id
            ORDER BY l.line_no
            """, new { Id = id }, cancellationToken: cancellationToken))).AsList();
        return voucher;
    }

    public async Task<long> SaveVoucherAsync(int organizationId, Voucher voucher, bool post, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new
        {
            Org = organizationId,
            voucher.Id,
            voucher.VoucherTypeId,
            voucher.VoucherDate,
            voucher.PaymentMethodId,
            voucher.ChequeNo,
            voucher.ChequeDate,
            voucher.PayType,
            voucher.Narration,
            TotalAmount = voucher.Lines.Sum(l => l.DebitAmount),
            UserId = userId,
            VoucherRefNo = voucher.Id == 0
                ? await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.Voucher, cancellationToken)
                : null
        };

        long id;
        HashSet<long> savedLineIds;
        if (voucher.Id == 0)
        {
            id = await connection.QuerySingleAsync<long>(new CommandDefinition($"""
                INSERT INTO accounting.voucher (
                    organization_id, voucher_ref_no, voucher_type_id, voucher_date, payment_method_id, cheque_no,
                    cheque_date, pay_type, narration, total_amount, status, created_at, created_by)
                VALUES (
                    @Org, @VoucherRefNo, @VoucherTypeId, @VoucherDate, @PaymentMethodId, @ChequeNo,
                    @ChequeDate, @PayType, @Narration, @TotalAmount, 'Draft', {SecurityDb.NowUtc}, @UserId)
                RETURNING voucher_id
                """, parameters, transaction, cancellationToken: cancellationToken));
            savedLineIds = [];
        }
        else
        {
            id = voucher.Id;
            await LockDraftAsync(connection, transaction, organizationId, id, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE accounting.voucher
                SET voucher_type_id = @VoucherTypeId, voucher_date = @VoucherDate, payment_method_id = @PaymentMethodId,
                    cheque_no = @ChequeNo, cheque_date = @ChequeDate, pay_type = @PayType, narration = @Narration,
                    total_amount = @TotalAmount, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE organization_id = @Org AND voucher_id = @Id
                """, parameters, transaction, cancellationToken: cancellationToken));
            savedLineIds = (await connection.QueryAsync<long>(new CommandDefinition(
                "SELECT voucher_line_id FROM accounting.voucher_line WHERE voucher_id = @Id",
                new { Id = id }, transaction, cancellationToken: cancellationToken))).ToHashSet();
        }

        // A line keeps its row when it names one of this draft's lines (once); any other line is new.
        var kept = new List<VoucherLine>();
        var added = new List<VoucherLine>();
        var claimed = new HashSet<long>();
        foreach (var line in voucher.Lines)
            (line.Id != 0 && savedLineIds.Contains(line.Id) && claimed.Add(line.Id) ? kept : added).Add(line);

        // ux_voucher_line is deferred, so renumbered lines can swap numbers before the commit.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM accounting.voucher_line WHERE voucher_id = @Id AND NOT (voucher_line_id = ANY(@KeptIds))",
            new { Id = id, KeptIds = claimed.ToArray() }, transaction, cancellationToken: cancellationToken));

        // Only lines that changed get the updated stamp.
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE accounting.voucher_line l
            SET line_no = x.line_no, acc_sub_class_id = x.acc_sub_class_id, party_id = x.party_id,
                debit_amount = x.debit_amount, credit_amount = x.credit_amount, lk_currency_id = x.lk_currency_id,
                local_exchange_rate = x.local_exchange_rate, line_narration = x.line_narration,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            FROM unnest(@LineIds, @LineNos, @AccountIds, @PartyIds, @Debits, @Credits, @Currencies, @Rates, @Narrations)
                 AS x(voucher_line_id, line_no, acc_sub_class_id, party_id, debit_amount, credit_amount, lk_currency_id,
                      local_exchange_rate, line_narration)
            WHERE l.voucher_id = @Id AND l.voucher_line_id = x.voucher_line_id
              AND (l.line_no, l.acc_sub_class_id, l.party_id, l.debit_amount, l.credit_amount, l.lk_currency_id,
                   l.local_exchange_rate, l.line_narration)
                  IS DISTINCT FROM (x.line_no, x.acc_sub_class_id, x.party_id, x.debit_amount, x.credit_amount,
                                    x.lk_currency_id, x.local_exchange_rate, x.line_narration)
            """, LineParameters(id, userId, kept), transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO accounting.voucher_line (voucher_id, line_no, acc_sub_class_id, party_id, debit_amount,
                                                 credit_amount, lk_currency_id, local_exchange_rate, line_narration,
                                                 created_at, created_by)
            SELECT @Id, x.line_no, x.acc_sub_class_id, x.party_id, x.debit_amount, x.credit_amount, x.lk_currency_id,
                   x.local_exchange_rate, x.line_narration, {SecurityDb.NowUtc}, @UserId
            FROM unnest(@LineIds, @LineNos, @AccountIds, @PartyIds, @Debits, @Credits, @Currencies, @Rates, @Narrations)
                 AS x(voucher_line_id, line_no, acc_sub_class_id, party_id, debit_amount, credit_amount, lk_currency_id,
                      local_exchange_rate, line_narration)
            """, LineParameters(id, userId, added), transaction, cancellationToken: cancellationToken));

        if (post)
            await PostAsync(connection, transaction, organizationId, id, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private static object LineParameters(long voucherId, int userId, IReadOnlyList<VoucherLine> lines) => new
    {
        Id = voucherId,
        UserId = userId,
        LineIds = lines.Select(l => l.Id).ToArray(),
        LineNos = lines.Select(l => l.LineNo).ToArray(),
        AccountIds = lines.Select(l => l.AccountId).ToArray(),
        PartyIds = lines.Select(l => l.PartyId).ToArray(),
        Debits = lines.Select(l => l.DebitAmount).ToArray(),
        Credits = lines.Select(l => l.CreditAmount).ToArray(),
        Currencies = lines.Select(l => l.CurrencyLookupDataId).ToArray(),
        Rates = lines.Select(l => l.LocalExchangeRate).ToArray(),
        Narrations = lines.Select(l => l.LineNarration).ToArray()
    };

    private static async Task<string> LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
               SELECT status FROM accounting.voucher
               WHERE organization_id = @Org AND voucher_id = @Id AND NOT is_deleted
               FOR UPDATE
               """, new { Org = organizationId, Id = id }, transaction, cancellationToken: cancellationToken))
           ?? throw new BusinessRuleException("The voucher doesn't exist.");

    private static async Task LockDraftAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, CancellationToken cancellationToken)
    {
        var status = await LockAsync(connection, transaction, organizationId, id, cancellationToken);
        if (status != DocumentStatus.Draft)
            throw new BusinessRuleException(status == DocumentStatus.Posted
                ? "The voucher is posted and can't be changed. Cancel it and enter a new one."
                : "The voucher is cancelled and can't be changed.");
    }

    private static async Task PostAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int organizationId, long id, int userId,
        CancellationToken cancellationToken)
    {
        var (lines, debit, credit) = await connection.QuerySingleAsync<(int, decimal, decimal)>(new CommandDefinition("""
            SELECT count(*)::int, coalesce(sum(debit_amount), 0), coalesce(sum(credit_amount), 0)
            FROM accounting.voucher_line WHERE voucher_id = @Id
            """, new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (lines < 2 || debit == 0)
            throw new BusinessRuleException("A voucher needs at least one debit and one credit line.");
        if (debit != credit)
            throw new BusinessRuleException($"Debits ({debit:N2}) and credits ({credit:N2}) must be equal to post the voucher.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE accounting.voucher
            SET status = 'Posted', total_amount = @Total, posted_at = {SecurityDb.NowUtc}, posted_by = @UserId
            WHERE organization_id = @Org AND voucher_id = @Id
            """, new { Org = organizationId, Id = id, Total = debit, UserId = userId }, transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        // The outer SELECT sees the row as it was before the UPDATE.
        var status = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition($"""
            WITH deleted AS (
                UPDATE accounting.voucher
                SET is_deleted = TRUE, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
                WHERE organization_id = @Org AND voucher_id = @Id AND status = 'Draft' AND NOT is_deleted
                RETURNING status)
            SELECT coalesce((SELECT status FROM deleted),
                            (SELECT status FROM accounting.voucher
                             WHERE organization_id = @Org AND voucher_id = @Id AND NOT is_deleted))
            """, new { Org = organizationId, Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return status switch
        {
            null => DeleteOutcome.NotFound,
            DocumentStatus.Draft => DeleteOutcome.Deleted,
            _ => DeleteOutcome.InUse
        };
    }

    public async Task PostVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockDraftAsync(connection, transaction, organizationId, id, cancellationToken);
        await PostAsync(connection, transaction, organizationId, id, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelVoucherAsync(int organizationId, long id, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await LockAsync(connection, transaction, organizationId, id, cancellationToken);
        if (status != DocumentStatus.Posted)
            throw new BusinessRuleException(status == DocumentStatus.Draft
                ? "Delete a draft voucher instead of cancelling it."
                : "This voucher is already cancelled.");

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE accounting.voucher
            SET status = 'Cancelled', cancelled_at = {SecurityDb.NowUtc}, cancelled_by = @UserId
            WHERE organization_id = @Org AND voucher_id = @Id
            """, new { Org = organizationId, Id = id, UserId = userId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}
