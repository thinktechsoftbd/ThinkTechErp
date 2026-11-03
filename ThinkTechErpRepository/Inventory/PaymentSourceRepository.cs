using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Inventory;

public interface IPaymentSourceRepository
{
    /// <summary>A page of sources, each with its lines; search matches the names and the lines' "payment for".</summary>
    Task<PagedResult<PaymentSource>> GetPaymentSourcesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<PaymentSource?> GetPaymentSourceAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreatePaymentSourceAsync(PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Saves the source and its lines: lines with an Id are updated in place, new ones added, and lines left out
    /// deleted. False when the source doesn't exist.
    /// </summary>
    Task<bool> UpdatePaymentSourceAsync(int id, PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>Deletes the source with its lines; InUse when another table still points at it.</summary>
    Task<DeleteOutcome> DeletePaymentSourceAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string nameEn, int? excludeId, CancellationToken cancellationToken = default);
    /// <summary>The ids of the source's lines, so a save can only keep lines that are its own.</summary>
    Task<IReadOnlyList<int>> GetLineIdsAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class PaymentSourceRepository(IDbConnectionFactory connectionFactory) : IPaymentSourceRepository
{
    private const string SourceSelect = """
        SELECT h.id AS Id, h.object_id AS ObjectId, h.source_name_en AS NameEn, h.source_name_loc AS NameLocal,
               h.is_active AS IsActive, h.created_at AT TIME ZONE 'utc' AS CreatedAt, h.created_by AS CreatedBy,
               h.updated_at AT TIME ZONE 'utc' AS UpdatedAt, h.updated_by AS UpdatedBy
        FROM public.inv_payment_source_h h
        """;

    private const string SourceFilter = """
        WHERE (CAST(@Pattern AS text) IS NULL OR h.source_name_en ILIKE @Pattern OR h.source_name_loc ILIKE @Pattern
               OR EXISTS (SELECT 1 FROM public.inv_payment_source_d d
                          WHERE d.inv_payment_source_h_id = h.id AND d.payment_for ILIKE @Pattern))
          AND (CAST(@IsActive AS boolean) IS NULL OR h.is_active = @IsActive)
        """;

    public async Task<PagedResult<PaymentSource>> GetPaymentSourcesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var page = await RepositoryHelpers.PagedAsync<PaymentSource>(connection,
            $"SELECT count(*) FROM public.inv_payment_source_h h {SourceFilter}",
            $"{SourceSelect} {SourceFilter} ORDER BY h.source_name_en, h.id",
            new { Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive }, limit, offset, cancellationToken);
        await AttachLinesAsync(connection, page.Items, cancellationToken);
        return page;
    }

    public async Task<PaymentSource?> GetPaymentSourceAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var source = await connection.QuerySingleOrDefaultAsync<PaymentSource>(new CommandDefinition(
            $"{SourceSelect} WHERE h.id = @Id", new { Id = id }, cancellationToken: cancellationToken));
        if (source is not null)
            await AttachLinesAsync(connection, [source], cancellationToken);
        return source;
    }

    /// <summary>Every line of these sources in one query, each with its ledger head.</summary>
    private static async Task AttachLinesAsync(
        System.Data.IDbConnection connection, IReadOnlyList<PaymentSource> sources, CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
            return;
        var lines = (await connection.QueryAsync<(int SourceId, int Id, string PaymentFor, int AccSubClassId, string? Code, string? Name, string? Path)>(
            new CommandDefinition("""
                SELECT d.inv_payment_source_h_id, d.id, d.payment_for, d.acc_sub_class_id, lh.sub_code, lh.sub_name,
                       concat_ws('/', lh.map_name, lh.main_name, lh.class_name, lh.parent_name)
                FROM public.inv_payment_source_d d
                LEFT JOIN public.ledger_head lh ON lh.acc_sub_class_id = d.acc_sub_class_id
                WHERE d.inv_payment_source_h_id = ANY(@Ids)
                ORDER BY d.id
                """, new { Ids = sources.Select(s => s.Id).ToArray() }, cancellationToken: cancellationToken)))
            .ToLookup(l => l.SourceId);
        foreach (var source in sources)
            source.Lines = lines[source.Id].Select(l => new PaymentSourceLine
            {
                Id = l.Id,
                PaymentFor = l.PaymentFor,
                AccSubClassId = l.AccSubClassId,
                AccountCode = l.Code ?? string.Empty,
                AccountName = l.Name ?? string.Empty,
                AccountPath = string.IsNullOrEmpty(l.Path) ? null : l.Path
            }).ToList();
    }

    public async Task<int> CreatePaymentSourceAsync(PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.inv_payment_source_h (source_name_en, source_name_loc, is_active, created_at, created_by)
            VALUES (@NameEn, @NameLocal, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, new { request.NameEn, request.NameLocal, request.IsActive, UserId = userId }, transaction,
            cancellationToken: cancellationToken));
        await InsertLinesAsync(connection, transaction, id, request.Lines, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdatePaymentSourceAsync(int id, PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.inv_payment_source_h
            SET source_name_en = @NameEn, source_name_loc = @NameLocal, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE id = @Id
            """, new { Id = id, request.NameEn, request.NameLocal, request.IsActive, UserId = userId }, transaction,
            cancellationToken: cancellationToken)) > 0;
        if (!updated)
            return false;

        var kept = request.Lines.Where(l => l.Id is not null).ToList();
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM public.inv_payment_source_d WHERE inv_payment_source_h_id = @Id AND id <> ALL(@Kept);
            UPDATE public.inv_payment_source_d d
            SET payment_for = x.payment_for, acc_sub_class_id = x.acc_sub_class_id
            FROM unnest(@Kept, @PaymentFors, @SubClassIds) AS x(id, payment_for, acc_sub_class_id)
            WHERE d.inv_payment_source_h_id = @Id AND d.id = x.id
            """, new
            {
                Id = id,
                Kept = kept.Select(l => l.Id!.Value).ToArray(),
                PaymentFors = kept.Select(l => l.PaymentFor).ToArray(),
                SubClassIds = kept.Select(l => l.AccSubClassId).ToArray()
            }, transaction, cancellationToken: cancellationToken));
        await InsertLinesAsync(connection, transaction, id, request.Lines.Where(l => l.Id is null).ToList(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static Task InsertLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int id, IReadOnlyList<PaymentSourceLineRequest> lines,
        CancellationToken cancellationToken)
        => lines.Count == 0
            ? Task.CompletedTask
            : connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.inv_payment_source_d (inv_payment_source_h_id, payment_for, acc_sub_class_id)
                SELECT @Id, x.payment_for, x.acc_sub_class_id
                FROM unnest(@PaymentFors, @SubClassIds) WITH ORDINALITY AS x(payment_for, acc_sub_class_id, n)
                ORDER BY x.n
                """, new
                {
                    Id = id,
                    PaymentFors = lines.Select(l => l.PaymentFor).ToArray(),
                    SubClassIds = lines.Select(l => l.AccSubClassId).ToArray()
                }, transaction, cancellationToken: cancellationToken));

    public async Task<DeleteOutcome> DeletePaymentSourceAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        // One statement, so the lines and the source go together (or neither, when something else uses them).
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition("""
            WITH lines AS (DELETE FROM public.inv_payment_source_d WHERE inv_payment_source_h_id = @Id)
            DELETE FROM public.inv_payment_source_h WHERE id = @Id
            """, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<bool> NameExistsAsync(string nameEn, int? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.inv_payment_source_h
                           WHERE lower(source_name_en) = lower(@NameEn) AND (CAST(@ExcludeId AS int) IS NULL OR id <> @ExcludeId))
            """, new { NameEn = nameEn, ExcludeId = excludeId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<int>> GetLineIdsAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT id FROM public.inv_payment_source_d WHERE inv_payment_source_h_id = @Id",
            new { Id = id }, cancellationToken: cancellationToken))).AsList();
    }
}
