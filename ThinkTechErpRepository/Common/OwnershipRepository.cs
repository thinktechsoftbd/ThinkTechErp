using Dapper;
using ThinkTechErpRepository.Database;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpRepository.Common;

/// <summary>Checks that ids sent by a client belong to the caller's organization (see OrganizationGuard).</summary>
public interface IOwnershipRepository
{
    /// <summary>Entities: unit, category, brand, warehouse, product, batch, taxGroup, taxRate, supplier.</summary>
    Task EnsureAsync(int organizationId, string entity, IEnumerable<long?> ids, CancellationToken cancellationToken = default);

    Task EnsureAsync(int organizationId, string entity, IEnumerable<int?> ids, CancellationToken cancellationToken = default);

    /// <summary>The batch ids among these that belong to the product.</summary>
    Task<bool> BatchesBelongToAsync(int organizationId, IEnumerable<(long BatchId, int ProductId)> pairs, CancellationToken cancellationToken = default);
}

public sealed class OwnershipRepository(IDbConnectionFactory connectionFactory) : IOwnershipRepository
{
    public async Task EnsureAsync(int organizationId, string entity, IEnumerable<long?> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        if (list.All(id => id is null))
            return;
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await OrganizationGuard.EnsureAsync(connection, null, organizationId, entity, list, cancellationToken);
    }

    public Task EnsureAsync(int organizationId, string entity, IEnumerable<int?> ids, CancellationToken cancellationToken = default)
        => EnsureAsync(organizationId, entity, ids.Select(id => (long?)id), cancellationToken);

    public async Task<bool> BatchesBelongToAsync(int organizationId, IEnumerable<(long BatchId, int ProductId)> pairs, CancellationToken cancellationToken = default)
    {
        var list = pairs.Distinct().ToList();
        if (list.Count == 0)
            return true;
        await using var connection = connectionFactory.CreateConnection();
        var found = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            SELECT count(*)::int FROM inventory.batch b
            JOIN unnest(@BatchIds, @ProductIds) AS x(batch_id, product_id) ON x.batch_id = b.batch_id AND x.product_id = b.product_id
            WHERE b.organization_id = @Org
            """, new
            {
                Org = organizationId,
                BatchIds = list.Select(p => p.BatchId).ToArray(),
                ProductIds = list.Select(p => p.ProductId).ToArray()
            }, cancellationToken: cancellationToken));
        return found == list.Count;
    }
}
