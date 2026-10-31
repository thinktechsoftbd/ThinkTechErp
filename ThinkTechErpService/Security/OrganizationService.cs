using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Security;

namespace ThinkTechErpService.Security;

public sealed class OrganizationService(IOrganizationRepository organizations) : IOrganizationService
{
    public Task<IReadOnlyList<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
        => organizations.GetOrganizationsAsync(cancellationToken);

    public Task<Organization?> GetOrganizationAsync(int organizationId, CancellationToken cancellationToken = default)
        => organizations.GetOrganizationAsync(organizationId, cancellationToken);

    public async Task<Organization> CreateOrganizationAsync(
        OrganizationUpsertRequest request, int actingUserId, CancellationToken cancellationToken = default)
    {
        Normalize(request);
        var organizationId = await SecurityDbErrors.ExecuteAsync(() =>
            organizations.CreateOrganizationAsync(request, actingUserId, cancellationToken));
        return await organizations.GetOrganizationAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("Created organization could not be read back.");
    }

    public async Task<Organization?> UpdateOrganizationAsync(
        int organizationId, OrganizationUpsertRequest request, int actingUserId,
        CancellationToken cancellationToken = default)
    {
        Normalize(request);
        if (!await SecurityDbErrors.ExecuteAsync(() =>
                organizations.UpdateOrganizationAsync(organizationId, request, actingUserId, cancellationToken)))
            return null;

        return await organizations.GetOrganizationAsync(organizationId, cancellationToken);
    }

    public async Task<ClientOrganizationSync> SyncClientOrganizationAsync(
        Guid objectId, OrganizationDetails details, int clientUserId, CancellationToken cancellationToken = default)
    {
        var existing = await organizations.GetOrganizationByObjectIdAsync(objectId, cancellationToken);
        if (existing is not null && existing.CreatedBy != clientUserId)
            return new ClientOrganizationSync(ClientOrganizationSyncStatus.NotOwned);

        // Active and External decide subscription validity, so they stay with the ERP.
        var request = new OrganizationUpsertRequest
        {
            ObjectId = objectId,
            NameEn = details.NameEn,
            NameBn = details.NameBn,
            Slogan = details.Slogan,
            Code = details.Code,
            Email = details.Email,
            Phone = details.Phone,
            Website = details.Website,
            AddressLine1 = details.AddressLine1,
            AddressLine2 = details.AddressLine2,
            City = details.City,
            State = details.State,
            PostalCode = details.PostalCode,
            Country = details.Country,
            LogoUrl = details.LogoUrl,
            IsExternal = existing?.IsExternal ?? true,
            IsActive = existing?.IsActive ?? true
        };

        if (existing is null)
            return new ClientOrganizationSync(ClientOrganizationSyncStatus.Created,
                await CreateOrganizationAsync(request, clientUserId, cancellationToken));

        var updated = await UpdateOrganizationAsync(existing.Id, request, clientUserId, cancellationToken)
            ?? throw new InvalidOperationException("Synced organization could not be read back.");
        return new ClientOrganizationSync(ClientOrganizationSyncStatus.Updated, updated);
    }

    // Blank optional fields are stored as NULL (organization_code is unique, so "" would clash).
    private static void Normalize(OrganizationUpsertRequest request)
    {
        if (request.ObjectId == Guid.Empty)
            throw new SecurityValidationException("The Organization Id can't be all zeros. Leave it empty to keep or generate one.");

        request.NameEn = request.NameEn.Trim();
        request.NameBn = Clean(request.NameBn);
        request.Slogan = Clean(request.Slogan);
        request.Code = Clean(request.Code);
        request.Email = Clean(request.Email);
        request.Phone = Clean(request.Phone);
        request.Website = Clean(request.Website);
        request.AddressLine1 = Clean(request.AddressLine1);
        request.AddressLine2 = Clean(request.AddressLine2);
        request.City = Clean(request.City);
        request.State = Clean(request.State);
        request.PostalCode = Clean(request.PostalCode);
        request.Country = Clean(request.Country);
        request.LogoUrl = Clean(request.LogoUrl);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
