using Dapper;
using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Accounting;

/// <summary>What vouchers and reports pick from: ledger heads (the acc_* sub classes), parties and lookup values.</summary>
public interface ILedgerLookupRepository
{
    /// <summary>Sub classes by code or name; with ids it returns exactly those heads.</summary>
    Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default);
    /// <summary>
    /// A page of sub classes for the ledger head pickers, matching the sub code or sub name; codes starting with
    /// the search come first, then by code.
    /// </summary>
    Task<PagedResult<LedgerHeadOption>> SearchLedgerHeadsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);
    /// <summary>The organization's suppliers (partyType Supplier), customers (Customer) or both (null).</summary>
    Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string? partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default);
    /// <summary>A lookup list's values by the list's name (Currency, Voucher Type, Payment Method), in entry order.</summary>
    Task<IReadOnlyList<LookupValueOption>> GetLookupValuesAsync(string lookupTable, CancellationToken cancellationToken = default);
}

public sealed class LedgerLookupRepository(IDbConnectionFactory connectionFactory) : ILedgerLookupRepository
{
    public async Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<LedgerHeadOption>(new CommandDefinition("""
            SELECT h.acc_sub_class_id AS Id, coalesce(h.sub_code, '') AS Code, coalesce(h.sub_name, '') AS Name,
                   coalesce(h.nature, '') AS Nature, concat_ws('/', h.map_name, h.main_name, h.class_name, h.parent_name) AS Path
            FROM public.ledger_head h
            WHERE (CAST(@Ids AS int[]) IS NULL OR h.acc_sub_class_id = ANY(@Ids))
              AND (CAST(@Pattern AS text) IS NULL OR h.sub_code ILIKE @Pattern OR h.sub_name ILIKE @Pattern)
            ORDER BY (h.sub_code ILIKE @Prefix) DESC, h.sub_code
            LIMIT @Limit
            """, new
            {
                Ids = ids?.ToArray(),
                Pattern = RepositoryHelpers.Pattern(search),
                Prefix = search is null ? null : search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%",
                Limit = limit
            }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<PagedResult<LedgerHeadOption>> SearchLedgerHeadsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM public.ledger_head h
            WHERE CAST(@Pattern AS text) IS NULL OR h.sub_code ILIKE @Pattern OR h.sub_name ILIKE @Pattern
            """;
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<LedgerHeadOption>(connection,
            $"SELECT count(*) {filter}",
            $"""
            SELECT h.acc_sub_class_id AS Id, coalesce(h.sub_code, '') AS Code, coalesce(h.sub_name, '') AS Name,
                   coalesce(h.nature, '') AS Nature, concat_ws('/', h.map_name, h.main_name, h.class_name, h.parent_name) AS Path
            {filter}
            ORDER BY (h.sub_code ILIKE @Prefix) DESC, h.sub_code, h.acc_sub_class_id
            """,
            new
            {
                Pattern = RepositoryHelpers.Pattern(search),
                Prefix = search is null ? null : search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%"
            }, limit, offset, cancellationToken);
    }

    public async Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string? partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default)
    {
        var side = partyType switch
        {
            PartyType.Supplier => "AND p.is_supplier",
            PartyType.Customer => "AND p.is_customer",
            _ => ""
        };
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<PartyOption>(new CommandDefinition($"""
            SELECT p.supplier_id AS Id, p.supplier_code AS Code, p.supplier_name AS Name, p.mobile AS Mobile,
                   p.is_supplier AS IsSupplier, p.is_customer AS IsCustomer, p.is_active AS IsActive
            FROM public.supplier p
            WHERE p.organization_id = @Org {side}
              AND (CAST(@Ids AS int[]) IS NULL OR p.supplier_id = ANY(@Ids))
              AND (CAST(@Pattern AS text) IS NULL OR p.supplier_code ILIKE @Pattern OR p.supplier_name ILIKE @Pattern
                   OR p.mobile ILIKE @Pattern)
            ORDER BY p.is_active DESC, p.supplier_name
            LIMIT @Limit
            """, new { Org = organizationId, Ids = ids?.ToArray(), Pattern = RepositoryHelpers.Pattern(search), Limit = limit },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<LookupValueOption>> GetLookupValuesAsync(string lookupTable, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<LookupValueOption>(new CommandDefinition("""
            SELECT d.lookup_data_id AS Id, d.lookup_data_name AS Name, d.is_active AS IsActive
            FROM public.lookup_data d
            JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
            WHERE lower(t.lookup_table_name) = lower(@Table)
            ORDER BY d.lookup_data_id
            """, new { Table = lookupTable }, cancellationToken: cancellationToken))).AsList();
    }
}
