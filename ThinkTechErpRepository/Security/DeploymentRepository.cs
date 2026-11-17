using Dapper;
using Npgsql;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Database;

namespace ThinkTechErpRepository.Security;

// public.deployment_info_h: what is deployed for an organization (organization_object_id), typed by the
// "Service Type" lookup list; public.deployment_info_d: its detail lines, deleted with it. Saving a deployment
// replaces its lines, which keep the order they were entered in (by id).
public sealed class DeploymentRepository(IDbConnectionFactory connectionFactory) : IDeploymentRepository
{
    private const string OrganizationSelect = """
        SELECT o.object_id AS ObjectId,
               o.organization_id AS Id,
               o.organization_name_en AS Name,
               o.organization_code AS Code,
               nullif(trim(o.organization_slug), '') AS Slug,
               o.is_active AS IsActive,
               o.is_external AS IsExternal,
               count(h.id)::int AS DeploymentCount,
               (count(h.id) FILTER (WHERE h.is_active))::int AS ActiveDeploymentCount,
               max(coalesce(h.updated_date, h.created_date)) AT TIME ZONE 'utc' AS LastChangedDate
        FROM public.organization o
        LEFT JOIN public.deployment_info_h h ON h.organization_object_id = o.object_id
        """;

    private const string OrganizationFilter = """
        WHERE CAST(@Pattern AS text) IS NULL OR o.organization_name_en ILIKE @Pattern
           OR o.organization_code ILIKE @Pattern OR o.organization_slug ILIKE @Pattern
        """;

    private const string DeploymentSelect = """
        SELECT h.id AS Id,
               h.object_id AS ObjectId,
               h.organization_object_id AS OrganizationObjectId,
               h.lk_service_type_id AS ServiceTypeId,
               st.lookup_data_name AS ServiceType,
               h.is_active AS IsActive,
               h.remarks AS Remarks,
               h.created_date AT TIME ZONE 'utc' AS CreatedDate,
               h.created_by_id AS CreatedById,
               cu.name AS CreatedByName,
               h.updated_date AT TIME ZONE 'utc' AS UpdatedDate,
               h.updated_by_id AS UpdatedById,
               uu.name AS UpdatedByName
        FROM public.deployment_info_h h
        JOIN public.lookup_data st ON st.lookup_data_id = h.lk_service_type_id
        LEFT JOIN public."User" cu ON cu.id = h.created_by_id
        LEFT JOIN public."User" uu ON uu.id = h.updated_by_id
        """;

    public async Task<PagedResult<DeploymentOrganization>> GetOrganizationsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            Pattern = search is null ? null : SecurityDb.ContainsPattern(search),
            Limit = limit,
            Offset = offset
        };

        await using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT count(*) FROM public.organization o {OrganizationFilter};
            {OrganizationSelect} {OrganizationFilter}
            GROUP BY o.organization_id
            ORDER BY o.organization_name_en, o.organization_id
            LIMIT @Limit OFFSET @Offset;
            """, parameters, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var items = (await results.ReadAsync<DeploymentOrganization>()).AsList();
        return new PagedResult<DeploymentOrganization> { Items = items, TotalCount = (int)totalCount, Limit = limit, Offset = offset };
    }

    public async Task<DeploymentOrganization?> GetOrganizationAsync(Guid organizationObjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<DeploymentOrganization>(new CommandDefinition($"""
            {OrganizationSelect}
            WHERE o.object_id = @ObjectId
            GROUP BY o.organization_id
            """, new { ObjectId = organizationObjectId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(
        Guid organizationObjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var deployments = (await connection.QueryAsync<DeploymentInfo>(new CommandDefinition($"""
            {DeploymentSelect}
            WHERE h.organization_object_id = @ObjectId
            ORDER BY st.lookup_data_name, h.id
            """, new { ObjectId = organizationObjectId }, cancellationToken: cancellationToken))).AsList();
        await AttachDetailsAsync(connection, deployments, cancellationToken);
        return deployments;
    }

    public async Task<DeploymentInfo?> GetDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var deployment = await connection.QuerySingleOrDefaultAsync<DeploymentInfo>(new CommandDefinition(
            $"{DeploymentSelect} WHERE h.id = @Id", new { Id = deploymentId }, cancellationToken: cancellationToken));
        if (deployment is not null)
            await AttachDetailsAsync(connection, [deployment], cancellationToken);
        return deployment;
    }

    public async Task<IReadOnlyList<DeploymentServiceType>> GetServiceTypesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<DeploymentServiceType>(new CommandDefinition("""
            SELECT d.lookup_data_id AS Id, d.lookup_data_name AS Name, d.is_active AS IsActive
            FROM public.lookup_data d
            JOIN public.lookup_table t ON t.lookup_table_id = d.lookup_table_id
            WHERE lower(t.lookup_table_name) = lower(@Table)
            ORDER BY d.lookup_data_id
            """, new { Table = DeploymentRules.ServiceTypeLookupTable }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<int> CreateDeploymentAsync(
        Guid organizationObjectId, DeploymentInfoUpsertRequest request, int actingUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition($"""
            INSERT INTO public.deployment_info_h (
                organization_object_id, lk_service_type_id, is_active, remarks, created_date, created_by_id)
            VALUES (@OrganizationObjectId, @ServiceTypeId, @IsActive, @Remarks, {SecurityDb.NowUtc}, @UserId)
            RETURNING id
            """, new
            {
                OrganizationObjectId = organizationObjectId,
                request.ServiceTypeId,
                request.IsActive,
                request.Remarks,
                UserId = actingUserId
            }, transaction, cancellationToken: cancellationToken));
        await InsertDetailsAsync(connection, transaction, id, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateDeploymentAsync(
        int deploymentId, DeploymentInfoUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE public.deployment_info_h
            SET lk_service_type_id = @ServiceTypeId,
                is_active = @IsActive,
                remarks = @Remarks,
                updated_date = {SecurityDb.NowUtc},
                updated_by_id = @UserId
            WHERE id = @Id
            """, new { Id = deploymentId, request.ServiceTypeId, request.IsActive, request.Remarks, UserId = actingUserId },
            transaction, cancellationToken: cancellationToken));
        if (updated == 0)
            return false;

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM public.deployment_info_d WHERE deployment_info_h_id = @Id",
            new { Id = deploymentId }, transaction, cancellationToken: cancellationToken));
        await InsertDetailsAsync(connection, transaction, deploymentId, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM public.deployment_info_h WHERE id = @Id",
            new { Id = deploymentId }, cancellationToken: cancellationToken)) > 0;
    }

    // In the order given, so the lines' ids keep it.
    private static Task InsertDetailsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int deploymentId,
        DeploymentInfoUpsertRequest request, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO public.deployment_info_d (deployment_info_h_id, deployment_description)
            SELECT @Id, x.description
            FROM unnest(@Descriptions) WITH ORDINALITY AS x(description, position)
            ORDER BY x.position
            """, new
            {
                Id = deploymentId,
                Descriptions = request.Details.Select(detail => detail.Description ?? string.Empty).ToArray()
            }, transaction, cancellationToken: cancellationToken));

    private static async Task AttachDetailsAsync(
        NpgsqlConnection connection, IReadOnlyList<DeploymentInfo> deployments, CancellationToken cancellationToken)
    {
        if (deployments.Count == 0)
            return;

        var rows = await connection.QueryAsync<DetailRow>(new CommandDefinition("""
            SELECT id AS Id, deployment_info_h_id AS DeploymentId, deployment_description AS Description
            FROM public.deployment_info_d
            WHERE deployment_info_h_id = ANY(@Ids)
            ORDER BY id
            """, new { Ids = deployments.Select(deployment => deployment.Id).ToArray() }, cancellationToken: cancellationToken));
        var byDeployment = rows.ToLookup(row => row.DeploymentId);
        foreach (var deployment in deployments)
            deployment.Details = byDeployment[deployment.Id]
                .Select(row => new DeploymentInfoDetail { Id = row.Id, Description = row.Description })
                .ToList();
    }

    private sealed class DetailRow
    {
        public int Id { get; set; }
        public int DeploymentId { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
