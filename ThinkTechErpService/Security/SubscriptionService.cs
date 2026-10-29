using ThinkTechErpDomain.Models.Security;
using ThinkTechErpDomain.Models.Settings;
using ThinkTechErpRepository.Security;
using ThinkTechErpRepository.Settings;

namespace ThinkTechErpService.Security;

public sealed class SubscriptionService(
    ISubscriptionRepository subscriptions,
    ISubscriptionModuleRepository modules,
    ILookupRepository lookups) : ISubscriptionService
{
    public Task<PagedResult<Subscription>> GetSubscriptionsAsync(
        SubscriptionFilter filter, int limit, int offset, CancellationToken cancellationToken = default)
    {
        filter.Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        return subscriptions.GetSubscriptionsAsync(filter, limit, offset, cancellationToken);
    }

    public Task<Subscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
        => subscriptions.GetSubscriptionAsync(subscriptionId, cancellationToken);

    public async Task<Subscription> CreateSubscriptionAsync(
        SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(request, null, cancellationToken);
        var subscriptionId = await SecurityDbErrors.ExecuteAsync(() =>
            subscriptions.CreateSubscriptionAsync(request, userId, cancellationToken));
        return await subscriptions.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException("Created subscription could not be read back.");
    }

    public async Task<Subscription?> UpdateSubscriptionAsync(
        long subscriptionId, SubscriptionUpsertRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await subscriptions.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (existing is null)
            return null;

        await PrepareAsync(request, existing, cancellationToken);
        if (!await SecurityDbErrors.ExecuteAsync(() =>
                subscriptions.UpdateSubscriptionAsync(subscriptionId, request, userId, cancellationToken)))
            return null;

        return await subscriptions.GetSubscriptionAsync(subscriptionId, cancellationToken);
    }

    public Task<bool> DeleteSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
        => subscriptions.DeleteSubscriptionAsync(subscriptionId, cancellationToken);

    public async Task<SubscriptionValidity?> CheckValidityAsync(
        Guid organizationObjectId, string? moduleName, CancellationToken cancellationToken = default)
    {
        var organization = await subscriptions.FindOrganizationAsync(organizationObjectId, cancellationToken);
        if (organization is null)
            return null;

        // Subscription dates have no time zone; compare them with today's date in UTC.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var validity = new SubscriptionValidity
        {
            OrganizationObjectId = organization.ObjectId,
            OrganizationName = organization.Name,
            CheckedOn = today
        };
        if (!organization.IsActive)
        {
            validity.Reason = "The organization is inactive.";
            return validity;
        }

        moduleName = string.IsNullOrWhiteSpace(moduleName) ? null : moduleName.Trim();
        var current = await subscriptions.GetCurrentSubscriptionsAsync(organization.Id, moduleName, today, cancellationToken);
        foreach (var subscription in current)
            subscription.DaysRemaining = subscription.EndDate.DayNumber - today.DayNumber;

        validity.Subscriptions = current.ToArray();
        validity.IsValid = current.Count > 0;
        if (validity.IsValid)
        {
            validity.ValidUntil = current.Max(subscription => subscription.EndDate);
            validity.DaysRemaining = validity.ValidUntil.Value.DayNumber - today.DayNumber;
        }
        else
        {
            validity.Reason = moduleName is null
                ? "No active subscription covers today."
                : $"No active subscription to {moduleName} covers today.";
        }

        return validity;
    }

    // Fills in the end date and discount amount, then checks the module, currency and dates.
    // The organization is checked by its foreign key.
    private async Task PrepareAsync(
        SubscriptionUpsertRequest request, Subscription? existing, CancellationToken cancellationToken)
    {
        var startDate = request.StartDate ?? throw new SecurityValidationException("Enter a start date.");
        request.EndDate ??= request.BillingPeriodType == "Yearly"
            ? startDate.AddYears(request.BillingPeriodCount).AddDays(-1)
            : startDate.AddMonths(request.BillingPeriodCount).AddDays(-1);
        if (request.EndDate < startDate)
            throw new SecurityValidationException("The end date can't be before the start date.");

        if (request.DiscountPercentage > 0)
            request.DiscountAmount = Math.Round(
                request.SubtotalAmount * request.DiscountPercentage / 100m, 2, MidpointRounding.AwayFromZero);
        if (request.DiscountAmount > request.SubtotalAmount)
            throw new SecurityValidationException("The discount can't be more than the subtotal.");

        // A new subscription needs an active module and currency; an existing one may keep a
        // module or currency that was deactivated after it was created.
        var module = await modules.GetModuleAsync(request.SubscriptionModuleId, cancellationToken)
            ?? throw new SecurityValidationException("The subscription module does not exist.");
        if (!module.IsActive && existing?.SubscriptionModuleId != module.Id)
            throw new SecurityValidationException($"The subscription module \"{module.ModuleName}\" is inactive.");

        var currency = await lookups.GetDataItemAsync(request.CurrencyLookupDataId, cancellationToken);
        if (currency is null ||
            !string.Equals(currency.LookupTableName, SettingsRules.CurrencyLookupTable, StringComparison.OrdinalIgnoreCase))
            throw new SecurityValidationException($"Choose a currency from the {SettingsRules.CurrencyLookupTable} lookup table.");
        if (!currency.IsActive && existing?.CurrencyLookupDataId != currency.Id)
            throw new SecurityValidationException($"The currency {currency.Name} is inactive.");

        if (request.Status is SubscriptionStatuses.Pending or SubscriptionStatuses.Active &&
            await subscriptions.HasOverlapAsync(request.OrganizationId, request.SubscriptionModuleId, startDate,
                request.EndDate.Value, existing?.Id, cancellationToken))
            throw new SecurityConflictException(
                "The organization already has a pending or active subscription to this module for these dates.");
    }
}
