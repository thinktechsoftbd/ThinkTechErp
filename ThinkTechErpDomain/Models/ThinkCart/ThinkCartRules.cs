using System.Text;

namespace ThinkTechErpDomain.Models.ThinkCart;

/// <summary>Limits and formats shared by the ThinkCart storefront API (api/thinkcart).</summary>
public static class ThinkCartRules
{
    /// <summary>A Bangladeshi mobile number as stored on web customers: 01XXXXXXXXX.</summary>
    public const string MobilePattern = "^01[3-9][0-9]{8}$";

    public const string EmailPattern = @"^[^\s@]+@[^\s@]+\.[^\s@]+$";

    public const int MinPasswordLength = 6;
    public const int MaxPasswordLength = 128;

    /// <summary>OTP codes: 6 digits, valid for 5 minutes, 5 wrong tries, one per minute and 5 an hour per mobile.</summary>
    public const int OtpLength = 6;
    public const int OtpLifetimeSeconds = 300;
    public const int OtpMaxAttempts = 5;
    public const int OtpResendSeconds = 60;
    public const int OtpMaxPerHour = 5;

    public const int MaxEventsPerRequest = 50;
    public const int MaxReviewImages = 3;
    public const int MaxCartQuantity = 10000;
    public const int SearchSuggestLimit = 8;
    public const int HomeTopSellingLimit = 12;
    public const int HomeCollectionLimit = 10;
    public const int TopSellingDays = 90;
    public const int FrequentlyBoughtTogetherLimit = 8;
    public const int RecentOrdersLimit = 5;

    /// <summary>Web customers get supplier_code 'WEB-' || mobile.</summary>
    public const string WebCustomerCodePrefix = "WEB-";

    /// <summary>Used when site_setting.shipping.default_zone_code is missing.</summary>
    public const string FallbackZoneCode = "outside_dhaka";

    public const int NameMaxLength = 250;
    public const int EmailMaxLength = 150;
    public const int AddressMaxLength = 250;
    public const int NoteMaxLength = 1000;
    public const int SlugMaxLength = 260;

    /// <summary>
    /// 01XXXXXXXXX from what a customer typed: spaces, dashes and brackets are dropped, and a
    /// +880 / 880 / 0088 country prefix is replaced by the leading 0. Null when it isn't a mobile number.
    /// </summary>
    public static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiDigit(character))
                digits.Append(character);
            else if (character is not (' ' or '-' or '(' or ')' or '.' or '+'))
                return null;
        }

        var number = digits.ToString();
        if (number.StartsWith("0088", StringComparison.Ordinal))
            number = number[4..];
        if (number.StartsWith("880", StringComparison.Ordinal) && number.Length == 13)
            number = number[2..];

        return number.Length == 11 && number[0] == '0' && number[1] == '1' && number[2] is >= '3' and <= '9'
            ? number
            : null;
    }
}

public static class OtpPurposes
{
    public const string Login = "login";
    public const string Register = "register";
    public const string ResetPassword = "reset_password";

    /// <summary>The purposes a customer may ask a code for (otp_code also allows order_verify).</summary>
    public static readonly IReadOnlySet<string> Requestable = new HashSet<string>(StringComparer.Ordinal)
        { Login, Register, ResetPassword };
}

public static class ProductSorts
{
    public const string Featured = "featured";
    public const string Newest = "newest";
    public const string PriceAsc = "price_asc";
    public const string PriceDesc = "price_desc";
    public const string BestSelling = "best_selling";
    public const string Name = "name";

    /// <summary>Internal: keep the order of ProductCardQuery.Ids.</summary>
    public const string IdOrder = "id_order";

    /// <summary>Internal: names starting with the search text first (search suggestions).</summary>
    public const string Suggest = "suggest";

    public static readonly IReadOnlySet<string> Public = new HashSet<string>(StringComparer.Ordinal)
        { Featured, Newest, PriceAsc, PriceDesc, BestSelling, Name };
}

public static class AnalyticsEventNames
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "page_view", "category_view", "product_view", "search",
        "add_to_cart", "remove_from_cart", "add_to_wishlist",
        "begin_checkout", "purchase", "click_whatsapp", "click_call"
    };
}

public static class CustomerGenders
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { "male", "female", "other" };
}

public static class EcomOrderStatuses
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { "pending", "on_hold", "confirmed", "processing", "shipped", "delivered", "cancelled", "returned" };

    /// <summary>Placed but not finished yet (the dashboard's pending count).</summary>
    public static readonly IReadOnlyList<string> Open = ["pending", "on_hold", "confirmed", "processing", "shipped"];
}

/// <summary>Effective-price helpers shared by cards, product pages, carts and orders.</summary>
public static class ThinkCartPricing
{
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>COALESCE(compare_at_price, mrp), only when it is above the price.</summary>
    public static decimal? CompareAt(decimal price, decimal? compareAtRaw)
        => compareAtRaw is decimal compareAt && compareAt > price ? Money(compareAt) : null;

    /// <summary>round((compareAt - price) / compareAt * 100), or null without a strike-through price.</summary>
    public static int? DiscountPercent(decimal price, decimal? compareAt)
        => compareAt is decimal value && value > 0 && value > price
            ? (int)Math.Round((value - price) / value * 100m, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>Drops trailing zeros (50.0000 becomes 50) so quantities read as plain JSON numbers.</summary>
    public static decimal Quantity(decimal value) => value / 1.0000000000000000000000000000m;
}
