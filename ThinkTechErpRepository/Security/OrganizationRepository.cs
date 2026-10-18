using Dapper;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

public sealed class OrganizationRepository(IDbConnectionFactory connectionFactory) : IOrganizationRepository
{
    private const string OrganizationSelect = """
        SELECT organization_id AS Id,
               organization_name_en AS NameEn,
               organization_name_bn AS NameBn,
               slogan AS Slogan,
               organization_code AS Code,
               email AS Email,
               phone AS Phone,
               website AS Website,
               address_line1 AS AddressLine1,
               address_line2 AS AddressLine2,
               city AS City,
               state AS State,
               postal_code AS PostalCode,
               country AS Country,
               logo_url AS LogoUrl,
               is_active AS IsActive,
               created_at AT TIME ZONE 'utc' AS CreatedAt,
               created_by AS CreatedBy,
               updated_at AT TIME ZONE 'utc' AS UpdatedAt,
               updated_by AS UpdatedBy
        FROM security.organization
        """;

    public async Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        var rows = await connection.QueryAsync<Organization>(new CommandDefinition(
            $"{OrganizationSelect} ORDER BY organization_name_en, organization_id", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleOrDefaultAsync<Organization>(new CommandDefinition(
            $"{OrganizationSelect} WHERE organization_id = @OrganizationId", new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CreateOrganizationAsync(
        OrganizationUpsertRequest request, int createdBy, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("UserId", createdBy);

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO security.organization (
                organization_name_en, organization_name_bn, slogan, organization_code, email, phone, website,
                address_line1, address_line2, city, state, postal_code, country, logo_url, is_active,
                created_at, created_by)
            VALUES (
                @NameEn, @NameBn, @Slogan, @Code, @Email, @Phone, @Website,
                @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @Country, @LogoUrl, @IsActive,
                {SecurityDb.NowUtc}, @UserId)
            RETURNING organization_id
            """, parameters, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateOrganizationAsync(
        int organizationId, OrganizationUpsertRequest request, int updatedBy, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(request);
        parameters.Add("OrganizationId", organizationId);
        parameters.Add("UserId", updatedBy);

        await using var connection = connectionFactory.CreateConnection(SecurityDb.Connection);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE security.organization
            SET organization_name_en = @NameEn,
                organization_name_bn = @NameBn,
                slogan = @Slogan,
                organization_code = @Code,
                email = @Email,
                phone = @Phone,
                website = @Website,
                address_line1 = @AddressLine1,
                address_line2 = @AddressLine2,
                city = @City,
                state = @State,
                postal_code = @PostalCode,
                country = @Country,
                logo_url = @LogoUrl,
                is_active = @IsActive,
                updated_at = {SecurityDb.NowUtc},
                updated_by = @UserId
            WHERE organization_id = @OrganizationId
            """, parameters, cancellationToken: cancellationToken)) > 0;
    }
}
