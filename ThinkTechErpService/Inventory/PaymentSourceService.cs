using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpRepository.Accounting;
using ThinkTechErpRepository.Inventory;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Inventory;

public interface IPaymentSourceService
{
    Task<PagedResult<PaymentSource>> GetPaymentSourcesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default);
    Task<PaymentSource?> GetPaymentSourceAsync(int id, CancellationToken cancellationToken = default);
    Task<PaymentSource> CreatePaymentSourceAsync(PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<PaymentSource?> UpdatePaymentSourceAsync(int id, PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default);
    Task<DeleteOutcome> DeletePaymentSourceAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class PaymentSourceService(IPaymentSourceRepository repository, ILedgerLookupRepository ledgerHeads)
    : IPaymentSourceService
{
    public Task<PagedResult<PaymentSource>> GetPaymentSourcesAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => repository.GetPaymentSourcesAsync(Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<PaymentSource?> GetPaymentSourceAsync(int id, CancellationToken cancellationToken = default)
        => repository.GetPaymentSourceAsync(id, cancellationToken);

    public async Task<PaymentSource> CreatePaymentSourceAsync(PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(null, request, cancellationToken);
        var id = await BusinessDbErrors.ExecuteAsync(() => repository.CreatePaymentSourceAsync(request, userId, cancellationToken));
        return (await repository.GetPaymentSourceAsync(id, cancellationToken))!;
    }

    public async Task<PaymentSource?> UpdatePaymentSourceAsync(int id, PaymentSourceUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (await repository.GetPaymentSourceAsync(id, cancellationToken) is null)
            return null;
        await NormalizeAsync(id, request, cancellationToken);
        return await BusinessDbErrors.ExecuteAsync(() => repository.UpdatePaymentSourceAsync(id, request, userId, cancellationToken))
            ? await repository.GetPaymentSourceAsync(id, cancellationToken)
            : null;
    }

    public Task<DeleteOutcome> DeletePaymentSourceAsync(int id, CancellationToken cancellationToken = default)
        => repository.DeletePaymentSourceAsync(id, cancellationToken);

    /// <summary>
    /// Trims the names; each line needs a "payment for" (once per source) and a ledger head of the chart.
    /// On a save, kept lines must be this source's own; on a create every line is new.
    /// </summary>
    private async Task NormalizeAsync(int? id, PaymentSourceUpsertRequest request, CancellationToken cancellationToken)
    {
        request.NameEn = request.NameEn.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
        if (request.NameEn.Length == 0)
            throw new BusinessRuleException("Enter the payment source's name.");
        if (await repository.NameExistsAsync(request.NameEn, id, cancellationToken))
            throw new BusinessConflictException("A payment source with this name already exists.");

        HashSet<int> ownLines = id is null ? [] : (await repository.GetLineIdsAsync(id.Value, cancellationToken)).ToHashSet();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var label = $"Line {index + 1}";
            line.PaymentFor = line.PaymentFor.Trim();
            if (line.PaymentFor.Length == 0)
                throw new BusinessRuleException($"{label}: enter what the payment is for.");
            if (!seen.Add(line.PaymentFor))
                throw new BusinessRuleException($"{label}: \"{line.PaymentFor}\" is listed twice. Keep one line for it.");
            if (id is null)
                line.Id = null;
            else if (line.Id is { } lineId && !ownLines.Contains(lineId))
                throw new BusinessRuleException($"{label} isn't one of this payment source's lines. Reload the page and try again.");
        }

        var heads = request.Lines.Select(l => l.AccSubClassId).Distinct().ToList();
        if (heads.Count == 0)
            return;
        var found = (await ledgerHeads.GetLedgerHeadsAsync(null, heads, heads.Count, cancellationToken)).Select(h => h.Id).ToHashSet();
        var missing = request.Lines.FindIndex(l => !found.Contains(l.AccSubClassId));
        if (missing >= 0)
            throw new BusinessRuleException($"Line {missing + 1}: choose a ledger head (a sub class) from the chart of accounts.");
    }
}
