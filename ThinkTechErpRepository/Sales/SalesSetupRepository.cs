using Dapper;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Sales;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Common;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Sales;

public interface ISalesSetupRepository
{
    Task<PagedResult<Customer>> GetCustomersAsync(int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<Customer?> GetCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default);
    /// <summary>Creates a customer; an empty Code takes the next one in the series (C-00001, ...).</summary>
    Task<int> CreateCustomerAsync(int organizationId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<bool> UpdateCustomerAsync(int organizationId, int customerId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    /// <summary>A customer that is also a supplier only stops being a customer; otherwise the row goes.</summary>
    Task<DeleteOutcome> DeleteCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);
    /// <summary>Suppliers and customers share one list of codes, so this checks both sides.</summary>
    Task<bool> CustomerCodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default);
    Task<bool> LookupValueExistsAsync(long lookupDataId, string lookupTableName, CancellationToken cancellationToken = default);
}

public sealed class SalesSetupRepository(IDbConnectionFactory connectionFactory) : ISalesSetupRepository
{
    private const string CustomerBalance = """
        coalesce((SELECT sum(l.debit_amount - l.credit_amount) FROM public.customer_ledger l
                  WHERE l.customer_id = c.supplier_id), 0)
        """;

    private const string CustomerSelect = $"""
        SELECT c.supplier_id AS Id, c.object_id AS ObjectId, c.supplier_code AS Code, c.supplier_name AS Name,
               c.supplier_name_local AS NameLocal, c.customer_group_lookup_data_id AS CustomerGroupLookupDataId,
               g.lookup_data_name AS CustomerGroup, c.contact_person AS ContactPerson, c.mobile AS Mobile,
               c.phone AS Phone, c.email AS Email, c.address_line1 AS AddressLine1, c.address_line2 AS AddressLine2,
               c.city AS City, c.state AS State, c.postal_code AS PostalCode, c.country_code AS CountryCode,
               c.national_id_no AS NationalIdNo, c.tax_registration_no AS TaxRegistrationNo,
               c.customer_payment_terms_days AS PaymentTermsDays, c.customer_credit_limit AS CreditLimit,
               c.customer_opening_balance AS OpeningBalance, c.customer_opening_balance_date AS OpeningBalanceDate,
               c.remarks AS Remarks, c.is_active AS IsActive, c.is_supplier AS IsSupplier, {CustomerBalance} AS Balance,
               c.created_at AT TIME ZONE 'utc' AS CreatedAt, c.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM public.supplier c
        LEFT JOIN public.lookup_data g ON g.lookup_data_id = c.customer_group_lookup_data_id
        """;

    // Suppliers and customers share public.supplier; the sales screens see the customer side.
    private const string CustomerFilter = """
        WHERE c.organization_id = @Org AND c.is_customer
          AND (CAST(@Pattern AS text) IS NULL OR c.supplier_code ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
               OR c.supplier_name_local ILIKE @Pattern OR c.mobile ILIKE @Pattern OR c.phone ILIKE @Pattern
               OR c.contact_person ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR c.is_active = @IsActive)
          AND (CAST(@GroupId AS bigint) IS NULL OR c.customer_group_lookup_data_id = @GroupId)
        """;

    public async Task<PagedResult<Customer>> GetCustomersAsync(
        int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await RepositoryHelpers.PagedAsync<Customer>(connection,
            $"SELECT count(*) FROM public.supplier c {CustomerFilter}",
            $"{CustomerSelect} {CustomerFilter} ORDER BY c.supplier_name, c.supplier_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive, GroupId = groupId },
            limit, offset, cancellationToken);
    }

    public async Task<Customer?> GetCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Customer>(new CommandDefinition(
            $"{CustomerSelect} WHERE c.organization_id = @Org AND c.supplier_id = @Id AND c.is_customer",
            new { Org = organizationId, Id = customerId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(
        int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        // Shops find customers by mobile number, so an exact mobile match comes first.
        return (await connection.QueryAsync<CustomerOption>(new CommandDefinition($"""
            SELECT c.supplier_id AS Id, c.supplier_code AS Code, c.supplier_name AS Name, c.mobile AS Mobile,
                   nullif(concat_ws(', ', c.address_line1, c.city), '') AS Address,
                   c.customer_payment_terms_days AS PaymentTermsDays, c.customer_credit_limit AS CreditLimit,
                   {CustomerBalance} AS Balance,
                   c.customer_credit_limit - {CustomerBalance} AS AvailableCredit, c.is_active AS IsActive
            FROM public.supplier c
            WHERE c.organization_id = @Org AND c.is_customer
              AND (CAST(@Ids AS int[]) IS NULL OR c.supplier_id = ANY(@Ids))
              AND (CAST(@Pattern AS text) IS NULL OR c.supplier_code ILIKE @Pattern OR c.supplier_name ILIKE @Pattern
                   OR c.supplier_name_local ILIKE @Pattern OR c.mobile ILIKE @Pattern)
            ORDER BY c.is_active DESC, (c.mobile = @Search) DESC, c.supplier_name
            LIMIT @Limit
            """, new
            {
                Org = organizationId,
                Ids = ids?.ToArray(),
                Search = search,
                Pattern = RepositoryHelpers.Pattern(search),
                Limit = limit
            }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<int> CreateCustomerAsync(int organizationId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            // Skip numbers already taken by codes typed by hand (suppliers share the same codes).
            string code;
            var attempts = 0;
            do
            {
                code = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.Customer, cancellationToken);
            } while (++attempts < 50 && await connection.QuerySingleAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM public.supplier WHERE organization_id = @Org AND lower(supplier_code) = lower(@Code))
                """, new { Org = organizationId, Code = code }, transaction, cancellationToken: cancellationToken)));
            request.Code = code;
        }

        var parameters = Params(organizationId, request, userId);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.supplier (
                organization_id, supplier_code, supplier_name, supplier_name_local, customer_group_lookup_data_id,
                contact_person, mobile, phone, email, address_line1, address_line2, city, state, postal_code, country_code,
                national_id_no, tax_registration_no, customer_payment_terms_days, customer_credit_limit,
                customer_opening_balance, customer_opening_balance_date, remarks, is_active, is_supplier, is_customer,
                created_at, created_by)
            VALUES (
                @Org, @Code, @Name, @NameLocal, @CustomerGroupLookupDataId,
                @ContactPerson, @Mobile, @Phone, @Email, @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @CountryCode,
                @NationalIdNo, @TaxRegistrationNo, @PaymentTermsDays, @CreditLimit,
                @OpeningBalance, @OpeningBalanceDate, @Remarks, @IsActive, @IsSupplier, TRUE,
                {SecurityDb.NowUtc}, @UserId)
            RETURNING supplier_id
            """, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateCustomerAsync(int organizationId, int customerId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", customerId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.supplier
            SET supplier_code = coalesce(@Code, supplier_code), supplier_name = @Name, supplier_name_local = @NameLocal,
                customer_group_lookup_data_id = @CustomerGroupLookupDataId, contact_person = @ContactPerson,
                mobile = @Mobile, phone = @Phone, email = @Email, address_line1 = @AddressLine1,
                address_line2 = @AddressLine2, city = @City, state = @State, postal_code = @PostalCode,
                country_code = @CountryCode, national_id_no = @NationalIdNo, tax_registration_no = @TaxRegistrationNo,
                customer_payment_terms_days = @PaymentTermsDays, customer_credit_limit = @CreditLimit,
                customer_opening_balance = @OpeningBalance, customer_opening_balance_date = @OpeningBalanceDate,
                remarks = @Remarks, is_active = @IsActive, is_supplier = @IsSupplier,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND supplier_id = @Id AND is_customer
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var parameters = new { Org = organizationId, Id = customerId };
        // A party that is also a supplier stays for the purchase side; it only stops being a customer.
        if (await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE public.supplier SET is_customer = FALSE
                WHERE organization_id = @Org AND supplier_id = @Id AND is_customer AND is_supplier
                """, parameters, cancellationToken: cancellationToken)) > 0)
            return DeleteOutcome.Deleted;
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM public.supplier WHERE organization_id = @Org AND supplier_id = @Id AND is_customer",
            parameters, cancellationToken: cancellationToken));
    }

    public async Task<bool> CustomerCodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM public.supplier WHERE organization_id = @Org AND lower(supplier_code) = lower(@Code)
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

    private static DynamicParameters Params(int organizationId, object request, int userId)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("Org", organizationId);
        parameters.Add("UserId", userId);
        return parameters;
    }
}
