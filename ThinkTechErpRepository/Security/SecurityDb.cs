namespace ThinkTechErpRepository.Security;

internal static class SecurityDb
{
    public const string Connection = "Security";

    // security.* timestamps are UTC "timestamp without time zone" values (as the existing
    // app writes them). Generate and compare them in SQL so the session time zone never matters.
    public const string NowUtc = "(now() AT TIME ZONE 'utc')";

    public static string ContainsPattern(string value)
        => "%" + value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
