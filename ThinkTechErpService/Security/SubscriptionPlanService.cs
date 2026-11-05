using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Security;
using ThinkTechErpRepository.Settings;
using ThinkTechErpService.Common;

namespace ThinkTechErpService.Security;

public sealed class SubscriptionPlanService(ISubscriptionPlanRepository plans, ILookupRepository lookups) : ISubscriptionPlanService
{
    private const int MaxFeatures = 100;
    private const int MaxDetailsPerFeature = 100;

    public Task<PagedResult<SubscriptionPlan>> GetPlansAsync(
        string? search, bool? isActive, int limit, int offset, CancellationToken cancellationToken = default)
        => plans.GetPlansAsync(Text.Clean(search), isActive, limit, offset, cancellationToken);

    public Task<SubscriptionPlan?> GetPlanAsync(int planId, CancellationToken cancellationToken = default)
        => plans.GetPlanAsync(planId, cancellationToken);

    public async Task<SubscriptionPlan> CreatePlanAsync(
        SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await NormalizeAsync(request, existing: null, cancellationToken);
        var planId = await SecurityDbErrors.ExecuteAsync(() => plans.CreatePlanAsync(request, userId, cancellationToken));
        return await plans.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException("Created subscription plan could not be read back.");
    }

    public async Task<SubscriptionPlan?> UpdatePlanAsync(
        int planId, SubscriptionPlanUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await plans.GetPlanAsync(planId, cancellationToken);
        if (existing is null)
            return null;

        await NormalizeAsync(request, existing, cancellationToken);
        if (!await SecurityDbErrors.ExecuteAsync(() => plans.UpdatePlanAsync(planId, request, userId, cancellationToken)))
            return null;
        return await plans.GetPlanAsync(planId, cancellationToken);
    }

    public Task<DeleteOutcome> DeletePlanAsync(int planId, CancellationToken cancellationToken = default)
        => plans.DeletePlanAsync(planId, cancellationToken);

    // Trims the text, works out the discount, and checks the currency and the features.
    private async Task NormalizeAsync(
        SubscriptionPlanUpsertRequest request, SubscriptionPlan? existing, CancellationToken cancellationToken)
    {
        request.NameEn = request.NameEn.Trim();
        request.NameLocal = Text.Clean(request.NameLocal);
        request.ShortNote = Text.Clean(request.ShortNote);
        if (request.NameEn.Length == 0)
            throw new SecurityValidationException("Enter the plan name.");

        // The percentage wins; a discount price alone gives the percentage; neither is no discount.
        if (request.DiscountPercentage > 0)
            request.DiscountPrice = Math.Round(
                request.RegularPrice * (100m - request.DiscountPercentage) / 100m, 2, MidpointRounding.AwayFromZero);
        else if (request.DiscountPrice is decimal price && price < request.RegularPrice)
            request.DiscountPercentage = Math.Round(
                (request.RegularPrice - price) * 100m / request.RegularPrice, 2, MidpointRounding.AwayFromZero);
        else
            request.DiscountPrice = request.RegularPrice;
        if (request.DiscountPrice > request.RegularPrice)
            throw new SecurityValidationException("The discount price can't be more than the regular price.");

        // A new plan needs an active currency; a saved one may keep a currency deactivated since.
        var currency = await lookups.GetDataItemAsync(request.CurrencyLookupDataId, cancellationToken);
        if (currency is null ||
            !string.Equals(currency.LookupTableName, SettingsRules.CurrencyLookupTable, StringComparison.OrdinalIgnoreCase))
            throw new SecurityValidationException($"Choose a currency from the {SettingsRules.CurrencyLookupTable} lookup table.");
        if (!currency.IsActive && existing?.CurrencyLookupDataId != currency.Id)
            throw new SecurityValidationException($"The currency {currency.Name} is inactive.");

        request.Features ??= [];
        if (request.Features.Count > MaxFeatures)
            throw new SecurityValidationException($"A plan can have up to {MaxFeatures} features.");
        for (var index = 0; index < request.Features.Count; index++)
        {
            var feature = request.Features[index];
            var label = $"Feature {index + 1}";
            feature.Title = feature.Title?.Trim() ?? string.Empty;
            if (feature.Title.Length == 0)
                throw new SecurityValidationException($"{label}: enter its title.");

            feature.Details ??= [];
            if (feature.Details.Count > MaxDetailsPerFeature)
                throw new SecurityValidationException($"{label}: up to {MaxDetailsPerFeature} details.");
            for (var line = 0; line < feature.Details.Count; line++)
            {
                var detail = feature.Details[line];
                detail.Description = detail.Description?.Trim() ?? string.Empty;
                if (detail.Description.Length == 0)
                    throw new SecurityValidationException($"{label}, detail {line + 1}: enter the description.");
            }
        }

        if (await plans.PlanNameExistsAsync(request.NameEn, existing?.Id, cancellationToken))
            throw new SecurityConflictException("A subscription plan with this name already exists.");
    }
}
