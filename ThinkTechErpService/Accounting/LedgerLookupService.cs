using ThinkTechErpDomain.Models.Accounting;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Accounting;

public interface ILedgerLookupService
{
    Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default);
    /// <summary>A page of ledger heads for the search-as-you-type pickers.</summary>
    Task<PagedResult<LedgerHeadOption>> SearchLedgerHeadsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string? partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default);
}

public sealed class LedgerLookupService(ILedgerLookupRepository repository) : ILedgerLookupService
{
    public Task<IReadOnlyList<LedgerHeadOption>> GetLedgerHeadsAsync(
        string? search, IReadOnlyCollection<int>? ids, int limit, CancellationToken cancellationToken = default)
        => repository.GetLedgerHeadsAsync(Text.Clean(search), ids is { Count: > 0 } ? ids : null, limit, cancellationToken);

    public Task<PagedResult<LedgerHeadOption>> SearchLedgerHeadsAsync(
        string? search, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.SearchLedgerHeadsAsync(Text.Clean(search), limit, offset, cancellationToken);

    public Task<IReadOnlyList<PartyOption>> GetPartiesAsync(
        int organizationId, string? partyType, string? search, IReadOnlyCollection<int>? ids, int limit,
        CancellationToken cancellationToken = default)
    {
        partyType = Text.Clean(partyType);
        if (partyType is not null && !PartyType.All.Contains(partyType))
            throw new BusinessRuleException("The party type must be Supplier or Customer.");
        return repository.GetPartiesAsync(organizationId, partyType, Text.Clean(search), ids is { Count: > 0 } ? ids : null,
            limit, cancellationToken);
    }
}
