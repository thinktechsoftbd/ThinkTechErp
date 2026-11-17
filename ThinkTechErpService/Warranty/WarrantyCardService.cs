using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Warranty;
using ThinkTechErpRepository.Warranty;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Warranty;

public interface IWarrantyCardService
{
    Task<PagedResult<WarrantyCard>> GetCardsAsync(int organizationId, WarrantyCardQuery query, CancellationToken cancellationToken = default);
    Task<WarrantyCard?> GetCardAsync(int organizationId, long id, CancellationToken cancellationToken = default);
    /// <summary>The counter's warranty check: cards, units on record and earlier jobs for a serial, card, invoice or mobile.</summary>
    Task<WarrantyCheckResult> CheckAsync(int organizationId, string query, CancellationToken cancellationToken = default);
    Task<WarrantyCard> CreateManualCardAsync(int organizationId, WarrantyCardCreateRequest request, int userId, CancellationToken cancellationToken = default);
    Task<WarrantyCard?> VoidCardAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default);
}

public sealed class WarrantyCardService(
    IWarrantyCardRepository cards,
    IWarrantySetupRepository setup,
    IServiceJobRepository jobs) : IWarrantyCardService
{
    public Task<PagedResult<WarrantyCard>> GetCardsAsync(int organizationId, WarrantyCardQuery query, CancellationToken cancellationToken = default)
    {
        query.Search = Text.Clean(query.Search);
        query.Status = Text.Clean(query.Status);
        query.Coverage = Text.Clean(query.Coverage);
        query.Source = Text.Clean(query.Source);
        return cards.GetCardsAsync(organizationId, query, cancellationToken);
    }

    public Task<WarrantyCard?> GetCardAsync(int organizationId, long id, CancellationToken cancellationToken = default)
        => cards.GetCardAsync(organizationId, id, cancellationToken);

    public async Task<WarrantyCheckResult> CheckAsync(int organizationId, string query, CancellationToken cancellationToken = default)
    {
        var q = Text.Clean(query) ?? throw new BusinessRuleException("Enter a serial no., warranty no., invoice no. or mobile number.");
        var found = await cards.FindCardsAsync(organizationId, q, cancellationToken);
        var serials = await cards.FindSerialsAsync(organizationId, q, cancellationToken);
        var history = await jobs.FindJobsAsync(organizationId, q, found.Select(c => c.Id).ToList(), cancellationToken);
        return new WarrantyCheckResult { Query = q, Cards = found.ToList(), Serials = serials.ToList(), Jobs = history.ToList() };
    }

    public async Task<WarrantyCard> CreateManualCardAsync(int organizationId, WarrantyCardCreateRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var (exists, productPolicyId) = await setup.GetProductPolicyAsync(organizationId, request.ProductId, cancellationToken);
        if (!exists)
            throw new BusinessRuleException("The product doesn't exist.");
        if (request.StartDate == default)
            throw new BusinessRuleException("Enter the date of sale.");

        var policyId = request.WarrantyPolicyId ?? productPolicyId;
        var policy = policyId is int id ? await setup.GetPolicyAsync(organizationId, id, cancellationToken) : null;
        if (policyId is not null && policy is null)
            throw new BusinessRuleException("The warranty policy doesn't exist.");

        DateOnly end;
        DateOnly? replacementEnd = request.ReplacementEndDate;
        if (request.EndDate is { } given)
            end = given;
        else if (policy is not null)
        {
            var period = WarrantyRules.Period(request.StartDate, policy.WarrantyMonths, policy.ReplacementDays);
            end = period.End;
            replacementEnd ??= period.ReplacementEnd;
        }
        else
            throw new BusinessRuleException("The product has no warranty policy: choose one, or enter when the warranty ends.");
        if (end < request.StartDate)
            throw new BusinessRuleException("The warranty can't end before the date of sale.");
        if (replacementEnd is { } r && (r < request.StartDate || r > end))
            throw new BusinessRuleException("The replacement period must end between the date of sale and the end of the warranty.");

        var customerName = Text.Clean(request.CustomerName);
        var customerMobile = Text.Clean(request.CustomerMobile);
        if (request.CustomerId is null && customerName is null && customerMobile is null)
            throw new BusinessRuleException("Pick the customer, or enter the customer's name or mobile number.");

        var serialNo = Text.Clean(request.SerialNo);
        long? serialNumberId = null;
        if (serialNo is not null)
        {
            if (request.Quantity != 1)
                throw new BusinessRuleException("A card with a serial number covers one unit.");
            var matches = await jobs.FindSerialNumbersAsync(organizationId, request.ProductId, serialNo, cancellationToken);
            if (matches.Count == 1)
                serialNumberId = matches[0].SerialNumberId;
        }

        var card = new WarrantyCard
        {
            ProductId = request.ProductId,
            SerialNumberId = serialNumberId,
            SerialNo = serialNo,
            Quantity = request.Quantity,
            CustomerId = request.CustomerId,
            CustomerName = customerName,
            CustomerMobile = customerMobile,
            WarrantyPolicyId = policy?.Id,
            StartDate = request.StartDate,
            ReplacementEndDate = replacementEnd,
            EndDate = end,
            CoversParts = request.CoversParts ?? policy?.CoversParts ?? true,
            CoversLabor = request.CoversLabor ?? policy?.CoversLabor ?? true,
            Terms = Text.Clean(request.Terms) ?? policy?.Terms,
            SupplierId = request.SupplierId,
            SupplierWarrantyEndDate = request.SupplierWarrantyEndDate,
            Remarks = Text.Clean(request.Remarks)
        };
        var cardId = await BusinessDbErrors.ExecuteAsync(() => cards.CreateManualCardAsync(organizationId, card, userId, cancellationToken));
        return (await cards.GetCardAsync(organizationId, cardId, cancellationToken))!;
    }

    public async Task<WarrantyCard?> VoidCardAsync(int organizationId, long id, string reason, int userId, CancellationToken cancellationToken = default)
    {
        var card = await cards.GetCardAsync(organizationId, id, cancellationToken);
        if (card is null)
            return null;
        if (card.Status != WarrantyCardStatus.Active)
            throw new BusinessRuleException($"Card {card.WarrantyNo} is already {card.Status.ToLowerInvariant()}.");
        var why = Text.Clean(reason) ?? throw new BusinessRuleException("Say why the warranty is void.");
        await cards.VoidCardAsync(organizationId, id, why, userId, cancellationToken);
        return await cards.GetCardAsync(organizationId, id, cancellationToken);
    }
}
