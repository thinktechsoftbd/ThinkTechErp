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
    Task<DeleteOutcome> DeleteCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default);
    Task<bool> CustomerCodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default);
    Task<bool> LookupValueExistsAsync(long lookupDataId, string lookupTableName, CancellationToken cancellationToken = default);
}

public sealed class SalesSetupRepository(IDbConnectionFactory connectionFactory) : ISalesSetupRepository
{
    private const string CustomerBalance = """
        coalesce((SELECT sum(l.debit_amount - l.credit_amount) FROM sales.customer_ledger l
                  WHERE l.customer_id = c.customer_id), 0)
        """;

    private const string CustomerSelect = $"""
        SELECT c.customer_id AS Id, c.object_id AS ObjectId, c.customer_code AS Code, c.customer_name AS Name,
               c.customer_name_local AS NameLocal, c.customer_group_lookup_data_id AS CustomerGroupLookupDataId,
               g.lookup_data_name AS CustomerGroup, c.contact_person AS ContactPerson, c.mobile AS Mobile,
               c.phone AS Phone, c.email AS Email, c.address_line1 AS AddressLine1, c.address_line2 AS AddressLine2,
               c.city AS City, c.state AS State, c.postal_code AS PostalCode, c.country_code AS CountryCode,
               c.national_id_no AS NationalIdNo, c.tax_registration_no AS TaxRegistrationNo,
               c.payment_terms_days AS PaymentTermsDays, c.credit_limit AS CreditLimit,
               c.opening_balance AS OpeningBalance, c.opening_balance_date AS OpeningBalanceDate,
               c.remarks AS Remarks, c.is_active AS IsActive, {CustomerBalance} AS Balance,
               c.created_at AT TIME ZONE 'utc' AS CreatedAt, c.updated_at AT TIME ZONE 'utc' AS UpdatedAt
        FROM sales.customer c
        LEFT JOIN public.lookup_data g ON g.lookup_data_id = c.customer_group_lookup_data_id
        """;

    private const string CustomerFilter = """
        WHERE c.organization_id = @Org
          AND (CAST(@Pattern AS text) IS NULL OR c.customer_code ILIKE @Pattern OR c.customer_name ILIKE @Pattern
               OR c.customer_name_local ILIKE @Pattern OR c.mobile ILIKE @Pattern OR c.phone ILIKE @Pattern
               OR c.contact_person ILIKE @Pattern)
          AND (CAST(@IsActive AS boolean) IS NULL OR c.is_active = @IsActive)
          AND (CAST(@GroupId AS bigint) IS NULL OR c.customer_group_lookup_data_id = @GroupId)
        """;

    public async Task<PagedResult<Customer>> GetCustomersAsync(
        int organizationId, string? search, bool? isActive, long? groupId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await RepositoryHelpers.PagedAsync<Customer>(connection,
            $"SELECT count(*) FROM sales.customer c {CustomerFilter}",
            $"{CustomerSelect} {CustomerFilter} ORDER BY c.customer_name, c.customer_id",
            new { Org = organizationId, Pattern = RepositoryHelpers.Pattern(search), IsActive = isActive, GroupId = groupId },
            limit, offset, cancellationToken);
    }

    public async Task<Customer?> GetCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<Customer>(new CommandDefinition(
            $"{CustomerSelect} WHERE c.organization_id = @Org AND c.customer_id = @Id",
            new { Org = organizationId, Id = customerId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<CustomerOption>> GetCustomerOptionsAsync(
        int organizationId, string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        // Shops find customers by mobile number, so an exact mobile match comes first.
        return (await connection.QueryAsync<CustomerOption>(new CommandDefinition($"""
            SELECT c.customer_id AS Id, c.customer_code AS Code, c.customer_name AS Name, c.mobile AS Mobile,
                   nullif(concat_ws(', ', c.address_line1, c.city), '') AS Address,
                   c.payment_terms_days AS PaymentTermsDays, c.credit_limit AS CreditLimit,
                   {CustomerBalance} AS Balance,
                   c.credit_limit - {CustomerBalance} AS AvailableCredit, c.is_active AS IsActive
            FROM sales.customer c
            WHERE c.organization_id = @Org
              AND (CAST(@Ids AS int[]) IS NULL OR c.customer_id = ANY(@Ids))
              AND (CAST(@Pattern AS text) IS NULL OR c.customer_code ILIKE @Pattern OR c.customer_name ILIKE @Pattern
                   OR c.customer_name_local ILIKE @Pattern OR c.mobile ILIKE @Pattern)
            ORDER BY c.is_active DESC, (c.mobile = @Search) DESC, c.customer_name
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
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            // Skip numbers already taken by codes typed by hand.
            string code;
            var attempts = 0;
            do
            {
                code = await DocumentNumbers.NextAsync(connection, transaction, organizationId, DocumentType.Customer, cancellationToken);
            } while (++attempts < 50 && await connection.QuerySingleAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM sales.customer WHERE organization_id = @Org AND lower(customer_code) = lower(@Code))
                """, new { Org = organizationId, Code = code }, transaction, cancellationToken: cancellationToken)));
            request.Code = code;
        }

        var parameters = Params(organizationId, request, userId);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO sales.customer (
                organization_id, customer_code, customer_name, customer_name_local, customer_group_lookup_data_id,
                contact_person, mobile, phone, email, address_line1, address_line2, city, state, postal_code, country_code,
                national_id_no, tax_registration_no, payment_terms_days, credit_limit, opening_balance,
                opening_balance_date, remarks, is_active, created_at, created_by)
            VALUES (
                @Org, @Code, @Name, @NameLocal, @CustomerGroupLookupDataId,
                @ContactPerson, @Mobile, @Phone, @Email, @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @CountryCode,
                @NationalIdNo, @TaxRegistrationNo, @PaymentTermsDays, @CreditLimit, @OpeningBalance,
                @OpeningBalanceDate, @Remarks, @IsActive, {SecurityDb.NowUtc}, @UserId)
            RETURNING customer_id
            """, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateCustomerAsync(int organizationId, int customerId, CustomerUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var parameters = Params(organizationId, request, userId);
        parameters.Add("Id", customerId);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE sales.customer
            SET customer_code = coalesce(@Code, customer_code), customer_name = @Name, customer_name_local = @NameLocal,
                customer_group_lookup_data_id = @CustomerGroupLookupDataId, contact_person = @ContactPerson,
                mobile = @Mobile, phone = @Phone, email = @Email, address_line1 = @AddressLine1,
                address_line2 = @AddressLine2, city = @City, state = @State, postal_code = @PostalCode,
                country_code = @CountryCode, national_id_no = @NationalIdNo, tax_registration_no = @TaxRegistrationNo,
                payment_terms_days = @PaymentTermsDays, credit_limit = @CreditLimit, opening_balance = @OpeningBalance,
                opening_balance_date = @OpeningBalanceDate, remarks = @Remarks, is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc}, updated_by = @UserId
            WHERE organization_id = @Org AND customer_id = @Id
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<DeleteOutcome> DeleteCustomerAsync(int organizationId, int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await RepositoryHelpers.DeleteAsync(connection, new CommandDefinition(
            "DELETE FROM sales.customer WHERE organization_id = @Org AND customer_id = @Id",
            new { Org = organizationId, Id = customerId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> CustomerCodeExistsAsync(int organizationId, string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM sales.customer WHERE organization_id = @Org AND lower(customer_code) = lower(@Code)
                           AND (CAST(@ExcludeId AS int) IS NULL OR customer_id <> @ExcludeId))
            """, new { Org = organizationId, Code = code, ExcludeId = excludeId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> LookupValueExistsAsync(long lookupDataId, string lookupTableName, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
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
