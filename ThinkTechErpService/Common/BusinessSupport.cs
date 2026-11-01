using Npgsql;
using ThinkTechErpDomain.Models.Common;
using ThinkTechErpDomain.Models.Inventory;
using ThinkTechErpDomain.Models.Purchase;

namespace ThinkTechErpService.Common;

/// <summary>Turns database constraint errors from the inventory and purchase schemas into readable 400/409 errors.</summary>
internal static class BusinessDbErrors
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["ux_unit_code"] = "A unit with this code already exists.",
        ["ux_category_name"] = "A category with this name already exists at the same level.",
        ["ux_category_code"] = "A category with this code already exists.",
        ["ux_brand_name"] = "A brand with this name already exists.",
        ["ux_warehouse_code"] = "A warehouse with this code already exists.",
        ["ux_product_code"] = "A product with this code (SKU) already exists.",
        ["ux_product_barcode"] = "This barcode is already used by another product.",
        ["ux_product_unit"] = "A unit is listed twice for this product.",
        ["ux_product_unit_purchase_default"] = "Only one unit can be the default purchase unit.",
        ["ux_product_unit_sale_default"] = "Only one unit can be the default sale unit.",
        ["ux_tax_rate_code"] = "A tax rate with this code already exists.",
        ["ux_tax_group_name"] = "A tax group with this name already exists.",
        ["pk_tax_group_rate"] = "A tax rate is listed twice in this group.",
        ["ux_supplier_code"] = "A supplier with this code already exists.",
        ["ux_purchase_invoice_supplier_invoice"] = "This supplier invoice number is already entered for the supplier.",
        ["ux_batch_no"] = "This batch number already exists for the product.",
        ["ux_serial_number"] = "One of the serial numbers already exists for this product.",
        ["ck_purchase_invoice_credit_supplier"] = "A credit purchase needs a supplier on file.",
        ["ck_purchase_invoice_cash_paid"] = "A cash purchase must be paid in full.",
        ["ck_purchase_invoice_warehouse"] = "Choose the warehouse the stock goes into.",
        ["ck_purchase_return_due_supplier"] = "A return deducted from dues needs a supplier on file.",
        ["ux_customer_code"] = "A customer with this code already exists.",
        ["sms_provider_provider_code_key"] = "An SMS provider with this code already exists.",
        ["ck_sales_invoice_credit_customer"] = "A credit sale needs a customer on file.",
        ["ck_sales_invoice_cash_paid"] = "A cash sale must be paid in full.",
        ["ck_sales_invoice_paid"] = "The amount paid must be between 0 and the total, with how it was paid.",
        ["ck_sales_return_due_customer"] = "A return deducted from dues needs a customer on file.",
        ["ck_customer_credit_limit"] = "The credit limit can't be negative.",
        ["ck_stock_transfer_warehouses"] = "The two warehouses must be different.",
        ["ck_product_expiry"] = "Only batch-tracked products can have an expiry date.",
        ["ck_product_tracking_stock"] = "Only stock products can be tracked by batch or serial number.",
        ["ck_category_parent"] = "A category can't be its own parent."
    };

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new BusinessConflictException(Message(exception, "The request conflicts with an existing record."), exception);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new BusinessRuleException(Message(exception, "A referenced record doesn't exist or is still in use."), exception);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.CheckViolation)
        {
            throw new BusinessRuleException(Message(exception, $"The values break a rule ({exception.ConstraintName})."), exception);
        }
    }

    public static async Task ExecuteAsync(Func<Task> operation)
        => await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        });

    private static string Message(PostgresException exception, string fallback)
        => exception.ConstraintName is not null && Messages.TryGetValue(exception.ConstraintName, out var message)
            ? message
            : fallback;
}

public sealed record TaxResult(decimal TaxableAmount, decimal TaxAmount, decimal NonRecoverableTax, List<LineTaxItem> Items);

/// <summary>Tax on an amount at a tax group's rates; compound rates apply on the amount plus the taxes before them.</summary>
public static class TaxCalculator
{
    /// <summary>The tax on 1 at these rates, e.g. 0.15 for VAT 15%, 0.38 for SD 20% + compound VAT 15%.</summary>
    public static decimal EffectiveRate(IReadOnlyList<TaxGroupRateItem> rates)
    {
        decimal total = 0;
        foreach (var rate in rates.OrderBy(r => r.SequenceNo))
            total += rate.RatePercent / 100m * (rate.IsCompound ? 1 + total : 1);
        return total;
    }

    /// <param name="amount">Net of discount; includes the tax when inclusive.</param>
    public static TaxResult Compute(decimal amount, TaxGroupOption? group, bool inclusive)
    {
        if (group is null || group.Rates.Count == 0)
            return new TaxResult(Round(amount), 0, 0, []);

        var ordered = group.Rates.OrderBy(r => r.SequenceNo).ToList();
        var factors = new List<decimal>();
        decimal running = 0;
        foreach (var rate in ordered)
        {
            var factor = rate.RatePercent / 100m * (rate.IsCompound ? 1 + running : 1);
            factors.Add(factor);
            running += factor;
        }

        var taxable = inclusive ? amount / (1 + running) : amount;
        var items = ordered.Select((rate, index) => new LineTaxItem
        {
            TaxRateId = rate.TaxRateId,
            TaxCode = rate.TaxCode,
            TaxableAmount = Round(taxable * (rate.IsCompound ? 1 + factors.Take(index).Sum() : 1)),
            TaxAmount = Round(taxable * factors[index]),
            IsRecoverable = rate.IsRecoverable
        }).ToList();
        return new TaxResult(Round(taxable), items.Sum(i => i.TaxAmount),
            items.Where(i => !i.IsRecoverable).Sum(i => i.TaxAmount), items);
    }

    public static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

internal static class Text
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
