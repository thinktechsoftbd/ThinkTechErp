using Dapper;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Purchase;

public interface IPurchaseSetupRepository
{
    Task<PagedResult<Supplier>> GetSuppliersAsync(int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Supplier?> GetSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierOption>> GetSupplierOptionsAsync(int organizationId, string? search, int limit, CancellationToken cancellationToken = default);
    Task<int> CreateSupplierAsync(int organizationId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateSupplierAsync(int organizationId, int supplierId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default);
    Task<bool> SupplierCodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default);
    Task<bool> LookupValueExistsAsync(long lookupDataId, string lookupTableName, CancellationToken cancellationToken = default);

    Task<PagedResult<TaxRate>> GetTaxRatesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<TaxRate?> GetTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default);
    Task<int> CreateTaxRateAsync(int organizationId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateTaxRateAsync(int organizationId, int taxRateId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default);

    Task<PagedResult<TaxGroup>> GetTaxGroupsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<TaxGroup?> GetTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default);
    Task<int> CreateTaxGroupAsync(int organizationId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateTaxGroupAsync(int organizationId, int taxGroupId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeleteTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default);
    Task<int> CountTaxRatesAsync(int organizationId, IReadOnlyCollection<int> taxRateIds, CancellationToken cancellationToken = default);
}

public sealed class PurchaseSetupRepository(IDbConnectionFactory connectionFactory) : IPurchaseSetupRepository
{
    // ---- Suppliers

    private const string SupplierBalance = """
        coalesce((SELECT sum(l.credit_amount - l.debit_amount) FROM purchase.supplier_ledger l
                  WHERE l.supplier_id = s.supplier_id), 0)
        """;

    private const string SupplierSelect = $"""
        SELECT s.supplier_id AS Id, s.object_id AS ObjectId, s.supplier_code AS Code, s.supplier_name AS Name,
               s.supplier_name_local AS NameLocal, s.supplier_group_lookup_data_id AS SupplierGroupLookupDataId,
               g.lookup_data_name AS SupplierGroup, s.contact_person AS ContactPerson, s.phone AS Phone,
               s.mobile AS Mobile, s.email AS Email, s.address_line1 AS AddressLine1, s.address_line2 AS AddressLine2,
               s.city AS City, s.state AS State, s.postal_code AS PostalCode, s.country_code AS CountryCode,
               s.tax_registration_no AS TaxRegistrationNo, s.tax_id_no AS TaxIdNo, s.trade_license_no AS TradeLicenseNo,
               s.regulatory_license_no AS RegulatoryLicenseNo, s.currency_lookup_data_id AS CurrencyLookupDataId,
               cur.lookup_data_name AS Currency, s.payment_terms_days AS PaymentTermsDays, s.credit_limit AS CreditLimit,
               s.opening_balance AS OpeningBalance, s.opening_balance_date AS OpeningBalanceDate,
               s.bank_name AS BankName, s.bank_branch AS BankBranch, s.bank_account_name AS BankAccountName,
               s.bank_account_no AS BankAccountNo, s.bank_routing_code AS BankRoutingCode,
               s.mobile_wallet_no AS MobileWalletNo, s.remarks AS Remarks, s.is_active AS IsActive,
               s.is_customer AS IsCustomer, {SupplierBalance} AS Balance,
               s.created_at AT TIME ZONE 'utc' AS CreatedAt, s.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM purchase.supplier s
        LEFT JOIN public.lookup_data g ON g.lookup_data_id = s.supplier_group_lookup_data_id
        LEFT JOIN public.lookup_data cur ON cur.lookup_data_id = s.currency_lookup_data_id
        """;

    // Suppliers and customers share purchase.supplier; the purchase screens see the supplier side.
    private const string SupplierFilter = """
        WHERE s.organization_id = @Org AND s.is_supplier
          AND (CAST(@Pattern AS text) IS NULL OR s.supplier_code ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
               OR s.supplier_name_local ILIKE @Pattern OR s.mobile ILIKE @Pattern OR s.phone ILIKE @Pattern
               OR s.contact_person ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR s.is_active = @IsActive)
          AND (CAST(@GroupId AS bigint) IS NULL OR s.supplier_group_lookup_data_id = @GroupId)
        """;

    public async Task<PagedResult<Supplier>> GetSuppliersAsync(
        int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Supplier>(connection,
            $"SELECT count(*) FROM purchase.supplier s {SupplierFilter}",
            $"{SupplierSelect} {SupplierFilter} ORDER BY s.supplier_name, s.supplier_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive, GroupId = groupId },
            limit, offset, cancellationToken);
    }

    public async Task<Supplier?> GetSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Supplier>(new CommandDefinition(
            $"{SupplierSelect} WHERE s.organization_id = @Org AND s.supplier_id = @Id AND s.is_supplier",
            new { Org = organizationId, Id = supplierId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SupplierOption>> GetSupplierOptionsAsync(
        int organizationId, string? search, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<SupplierOption>(new CommandDefinition($"""
            SELECT s.supplier_id AS Id, s.supplier_code AS Code, s.supplier_name AS Name, s.mobile AS Mobile,
                   s.payment_terms_days AS PaymentTermsDays, s.currency_lookup_data_id AS CurrencyLookupDataId,
                   {SupplierBalance} AS Balance, s.is_active AS IsActive
            FROM purchase.supplier s
            WHERE s.organization_id = @Org AND s.is_supplier
              AND (CAST(@Pattern AS text) IS NULL OR s.supplier_code ILIKE @Pattern OR s.supplier_name ILIKE @Pattern
                   OR s.mobile ILIKE @Pattern)
            ORDER BY s.is_active DESC, s.supplier_name
            LIMIT @Limit
            """, new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), Limit = limit },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<int> CreateSupplierAsync(int organizationId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO purchase.supplier (
                organization_id, supplier_code, supplier_name, supplier_name_local, supplier_group_lookup_data_id,
                contact_person, phone, mobile, email, address_line1, address_line2, city, state, postal_code, country_code,
                tax_registration_no, tax_id_no, trade_license_no, regulatory_license_no, currency_lookup_data_id,
                payment_terms_days, credit_limit, opening_balance, opening_balance_date, bank_name, bank_branch,
                bank_account_name, bank_account_no, bank_routing_code, mobile_wallet_no, remarks, is_active,
                is_supplier, is_customer, created_at, created_by)
            VALUES (
                @Org, @Code, @Name, @NameLocal, @SupplierGroupLookupDataId,
                @ContactPerson, @Phone, @Mobile, @Email, @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @CountryCode,
                @TaxRegistrationNo, @TaxIdNo, @TradeLicenseNo, @RegulatoryLicenseNo, @CurrencyLookupDataId,
                @PaymentTermsDays, @CreditLimit, @OpeningBalance, @OpeningBalanceDate, @BankName, @BankBranch,
                @BankAccountName, @BankAccountNo, @BankRoutingCode, @MobileWalletNo, @Remarks, @IsActive,
                TRUE, @IsCustomer, {SecurityDb.NowUtc}, @UserId)
            RETURNING supplier_id
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateSupplierAsync(int organizationId, int supplierId, SupplierUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", supplierId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE purchase.supplier
            SET supplier_code = @Code, supplier_name = @Name, supplier_name_local = @NameLocal,
                supplier_group_lookup_data_id = @SupplierGroupLookupDataId, contact_person = @ContactPerson,
                phone = @Phone, mobile = @Mobile, email = @Email, address_line1 = @AddressLine1,
                address_line2 = @AddressLine2, city = @City, state = @State, postal_code = @PostalCode,
                country_code = @CountryCode, tax_registration_no = @TaxRegistrationNo, tax_id_no = @TaxIdNo,
                trade_license_no = @TradeLicenseNo, regulatory_license_no = @RegulatoryLicenseNo,
                currency_lookup_data_id = @CurrencyLookupDataId, payment_terms_days = @PaymentTermsDays,
                credit_limit = @CreditLimit, opening_balance = @OpeningBalance,
                opening_balance_date = @OpeningBalanceDate, bank_name = @BankName, bank_branch = @BankBranch,
                bank_account_name = @BankAccountName, bank_account_no = @BankAccountNo,
                bank_routing_code = @BankRoutingCode, mobile_wallet_no = @MobileWalletNo, remarks = @Remarks,
                is_active = @IsActive, is_customer = @IsCustomer, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND supplier_id = @Id AND is_supplier
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteSupplierAsync(int organizationId, int supplierId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = new { Org = organizationId, Id = supplierId };
        // A party that is also a customer stays for the sales side; it only stops being a supplier.
        if (await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE purchase.supplier SET is_supplier = FALSE
                WHERE organization_id = @Org AND supplier_id = @Id AND is_supplier AND is_customer
                """, parameters, cancellationToken: cancellationToken)) > 0)
            return DeleteOutcome.Deleted;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM purchase.supplier WHERE organization_id = @Org AND supplier_id = @Id AND is_supplier",
            parameters, cancellationToken: cancellationToken));
    }

    public async Task<bool> SupplierCodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM purchase.supplier WHERE organization_id = @Org AND lower(supplier_code) = lower(@Code)
                           AND (CAST(@ExcludeId AS int) IS NULL OR supplier_id <> @ExcludeId))
            """, new { Org = organizationId, Code = code, ExcludeId = excludeId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> LookupValueExistsAsync(long lookupDataId, string lookupTableName, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.lookup_data d JOIN public.lookup_table t USING (lookup_table_id)
                           WHERE d.lookup_data_id = @Id AND lower(t.lookup_table_name) = lower(@Table))
            """, new { Id = lookupDataId, Table = lookupTableName }, cancellationToken: cancellationToken));
    }

    // ---- Tax rates

    private const string TaxRateSelect = """
        SELECT r.tax_rate_id AS Id, r.tax_code AS Code, r.tax_name AS Name, r.tax_type AS TaxType,
               r.rate_percent AS RatePercent, r.is_recoverable AS IsRecoverable, r.is_active AS IsActive,
               (SELECT count(*) FROM public.tax_group_rate gr WHERE gr.tax_rate_id = r.tax_rate_id)::int AS GroupCount,
               r.created_at AT TIME ZONE 'utc' AS CreatedAt, r.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.tax_rate r
        """;

    private const string TaxRateFilter = """
        WHERE r.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR r.tax_code ILIKE @Pattern OR r.tax_name ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR r.is_active = @IsActive)
        """;

    public async Task<PagedResult<TaxRate>> GetTaxRatesAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<TaxRate>(connection,
            $"SELECT count(*) FROM public.tax_rate r {TaxRateFilter}",
            $"{TaxRateSelect} {TaxRateFilter} ORDER BY r.tax_type, r.rate_percent, r.tax_code",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive },
            limit, offset, cancellationToken);
    }

    public async Task<TaxRate?> GetTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<TaxRate>(new CommandDefinition(
            $"{TaxRateSelect} WHERE r.organization_id = @Org AND r.tax_rate_id = @Id",
            new { Org = organizationId, Id = taxRateId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CreateTaxRateAsync(int organizationId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.tax_rate (organization_id, tax_code, tax_name, tax_type, rate_percent, is_recoverable,
                                         is_active, created_at, created_by)
            VALUES (@Org, @Code, @Name, @TaxType, @RatePercent, @IsRecoverable, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING tax_rate_id
            """, Params(organizationId, request, userId), cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateTaxRateAsync(int organizationId, int taxRateId, TaxRateUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", taxRateId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.tax_rate
            SET tax_code = @Code, tax_name = @Name, tax_type = @TaxType, rate_percent = @RatePercent,
                is_recoverable = @IsRecoverable, is_active = @IsActive, updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND tax_rate_id = @Id
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteTaxRateAsync(int organizationId, int taxRateId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.tax_rate WHERE organization_id = @Org AND tax_rate_id = @Id",
            new { Org = organizationId, Id = taxRateId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CountTaxRatesAsync(int organizationId, IReadOnlyCollection<int> taxRateIds, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM public.tax_rate WHERE organization_id = @Org AND tax_rate_id = ANY(@Ids)",
            new { Org = organizationId, Ids = taxRateIds.Distinct().ToArray() }, cancellationToken: cancellationToken));
    }

    // ---- Tax groups

    private const string TaxGroupSelect = """
        SELECT g.tax_group_id AS Id, g.tax_group_name AS Name, g.is_active AS IsActive,
               g.created_at AT TIME ZONE 'utc' AS CreatedAt, g.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.tax_group g
        """;

    private const string TaxGroupFilter = """
        WHERE g.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR g.tax_group_name ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR g.is_active = @IsActive)
        """;

    public async Task<PagedResult<TaxGroup>> GetTaxGroupsAsync(int organizationId, string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var page = await RepositoryHelpers.PagedAsync<TaxGroup>(connection,
            $"SELECT count(*) FROM public.tax_group g {TaxGroupFilter}",
            $"{TaxGroupSelect} {TaxGroupFilter} ORDER BY g.tax_group_name, g.tax_group_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive },
            limit, offset, cancellationToken);
        await AttachRatesAsync(connection, page.Items, cancellationToken);
        return page;
    }

    public async Task<TaxGroup?> GetTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var group = await connection.QuerySingleOrDefaultAsync<TaxGroup>(new CommandDefinition(
            $"{TaxGroupSelect} WHERE g.organization_id = @Org AND g.tax_group_id = @Id",
            new { Org = organizationId, Id = taxGroupId }, cancellationToken: cancellationToken));
        if (group is not null)
            await AttachRatesAsync(connection, [group], cancellationToken);
        return group;
    }

    private static async Task AttachRatesAsync(System.Data.IDbConnection connection, IReadOnlyList<TaxGroup> groups, CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
            return;
        var rows = await connection.QueryAsync<int, TaxGroupRateItem, (int GroupId, TaxGroupRateItem Rate)>(new CommandDefinition("""
            SELECT gr.tax_group_id AS GroupId, r.tax_rate_id AS TaxRateId, r.tax_code AS TaxCode, r.tax_name AS TaxName,
                   r.tax_type AS TaxType, r.rate_percent AS RatePercent, r.is_recoverable AS IsRecoverable,
                   gr.is_compound AS IsCompound, gr.sequence_no AS SequenceNo
            FROM public.tax_group_rate gr JOIN public.tax_rate r ON r.tax_rate_id = gr.tax_rate_id
            WHERE gr.tax_group_id = ANY(@Ids)
            ORDER BY gr.tax_group_id, gr.sequence_no
            """, new { Ids = groups.Select(g => g.Id).ToArray() }, cancellationToken: cancellationToken),
            (groupId, rate) => (groupId, rate), splitOn: "TaxRateId");
        var byGroup = rows.ToLookup(r => r.GroupId, r => r.Rate);
        foreach (var group in groups)
            group.Rates = byGroup[group.Id].ToList();
    }

    public async Task<int> CreateTaxGroupAsync(int organizationId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.tax_group (organization_id, tax_group_name, is_active, created_at, created_by)
            VALUES (@Org, @Name, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING tax_group_id
            """, new { Org = organizationId, request.Name, request.IsActive, UserId = userId }, transaction,
            cancellationToken: cancellationToken));
        await WriteRatesAsync(connection, transaction, id, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateTaxGroupAsync(int organizationId, int taxGroupId, TaxGroupUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.tax_group SET tax_group_name = @Name, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND tax_group_id = @Id
            """, new { Org = organizationId, Id = taxGroupId, request.Name, request.IsActive, UserId = userId }, transaction,
            cancellationToken: cancellationToken)) > 0;
        if (!updated)
            return false;
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.tax_group_rate WHERE tax_group_id = @Id",
            new { Id = taxGroupId }, transaction, cancellationToken: cancellationToken));
        await WriteRatesAsync(connection, transaction, taxGroupId, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task WriteRatesAsync(
        Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction, int taxGroupId, TaxGroupUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Rates.Count == 0)
            return;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.tax_group_rate (tax_group_id, tax_rate_id, sequence_no, is_compound)
            VALUES (@GroupId, @TaxRateId, @SequenceNo, @IsCompound)
            """, request.Rates.Select((rate, index) => new
            {
                GroupId = taxGroupId,
                rate.TaxRateId,
                SequenceNo = (short)(index + 1),
                rate.IsCompound
            }), transaction, cancellationToken: cancellationToken));
    }

    public async Task<DeleteOutcome> DeleteTaxGroupAsync(int organizationId, int taxGroupId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.tax_group WHERE organization_id = @Org AND tax_group_id = @Id",
            new { Org = organizationId, Id = taxGroupId }, cancellationToken: cancellationToken));
    }

    private static DynamicParameters Params(int organizationId, object request, int userId)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("Org", organizationId);
        parameters.Add("UserId", userId);
        return parameters;
    }
}
